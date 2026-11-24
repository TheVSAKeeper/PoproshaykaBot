using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public sealed class TwitchAccountLogin(
    SettingsManager? settingsManager,
    AccountsStore accountsStore,
    ITwitchOAuthService? twitchOAuth,
    IShellLauncher? shellLauncher,
    TimeProvider timeProvider,
    IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
    IPlatformWebChatLoginDialog? webChatLoginDialog = null,
    IEventBus? eventBus = null,
    ILogger<TwitchAccountLogin>? logger = null) : IPlatformAccountLogin
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public bool CanAuthorize => twitchOAuth is not null && shellLauncher is not null;

    public bool SupportsEmbeddedBrowser => embeddedTwitchAuth is not null;

    public bool SupportsTokenValidation => true;

    public bool SupportsEditableScopes => true;

    public PlatformWebChatLoginDescriptor? WebChatDescriptor =>
        webChatLoginDialog is not null ? PlatformWebChatDescriptors.Twitch : null;

    public PlatformOAuthMethodDescriptor OAuthMethod => new(
        SupportsEmbeddedBrowser ? "Встроенный или системный браузер" : "Системный браузер",
        "Можно войти во встроенном окне приложения или через системный браузер.",
        SupportsEmbeddedBrowser,
        SupportsSystemBrowser: true);

    public IReadOnlyList<string> GetDefaultScopes(PlatformAuthRole role) =>
        role == PlatformAuthRole.Bot
            ? [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional]
            : [.. TwitchScopes.BroadcasterRequired];

    public string[] MergeRequestedScopes(PlatformAuthRole role, IReadOnlyCollection<string> configuredScopes)
    {
        var required = role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired;
        return [.. required.Union(configuredScopes, StringComparer.Ordinal)];
    }

    public async Task<PlatformAccountLoginResult> AuthorizeAsync(
        PlatformAccountLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (twitchOAuth is null || shellLauncher is null)
        {
            return new("Авторизация Twitch недоступна.", StatusSeverity.Warning, []);
        }

        var settings = settingsManager?.Current.Twitch;
        var clientId = settings?.ClientId ?? string.Empty;
        var clientSecret = settings?.ClientSecret ?? string.Empty;
        var redirectUri = settings?.RedirectUri ?? string.Empty;
        if (settingsManager is not null && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
        {
            return new("Заполните и сохраните ключи во вкладке «Параметры и доступ».", StatusSeverity.Warning, []);
        }

        var roles = request.Roles.Count > 0 ? request.Roles : [PlatformAuthRole.Bot];
        var combined = roles.Count > 1;
        var primaryRole = roles[0];

        string[] scopes = combined
            ? [.. TwitchScopes.CombinedDefault]
            : request.Scopes.Count > 0
                ? [.. request.Scopes]
                : [.. GetDefaultScopes(primaryRole)];

        OAuthFlowResult? flow = null;
        var usedEmbeddedBrowser = false;
        if (!request.UseSystemBrowser && embeddedTwitchAuth is not null)
        {
            var embedded = await embeddedTwitchAuth.AuthorizeAsync(new(
                primaryRole,
                clientId,
                clientSecret,
                scopes,
                redirectUri,
                CheckBroadcasterChannel: false));
            if (embedded.Outcome != EmbeddedTwitchAuthOutcome.Completed || embedded.Flow is null)
            {
                var severity = embedded.Outcome switch
                {
                    EmbeddedTwitchAuthOutcome.Failed => StatusSeverity.Error,
                    EmbeddedTwitchAuthOutcome.Unavailable => StatusSeverity.Warning,
                    _ => StatusSeverity.Info,
                };
                return new(embedded.Message, severity, []);
            }

            flow = embedded.Flow;
            usedEmbeddedBrowser = true;
        }

        flow ??= await twitchOAuth.StartOAuthFlowToDraftAsync(
            primaryRole,
            clientId,
            clientSecret,
            scopes,
            redirectUri,
            url => shellLauncher.Open(url),
            checkBroadcasterChannel: false,
            ct: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(flow.UserId))
        {
            return new("Twitch не сообщил ID аккаунта.", StatusSeverity.Error, []);
        }

        var userId = flow.UserId.Trim();
        if (!combined && !string.IsNullOrWhiteSpace(request.AccountId)
            && !string.Equals(request.AccountId, userId, StringComparison.Ordinal))
        {
            return new(
                $"Вы вошли в другой аккаунт («{flow.Login}»), а права запрашивались для другого аккаунта. Изменения не сохранены.",
                StatusSeverity.Warning,
                []);
        }

        var now = timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        var existingAccounts = accountsStore.ListAccounts();
        var existingBrowserProfileName = existingAccounts
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, userId, StringComparison.Ordinal))
            ?.Authorizations
            .Select(static g => g.ChatBrowserProfile?.ProfileName)
            .FirstOrDefault(static profile => !string.IsNullOrWhiteSpace(profile));
        List<(PlatformAuthRole Role, string ContextId, string Label, TwitchAccountSettings Account)> entriesToSave = [];
        List<PlatformAuthRole> missingRoles = [];

        foreach (var targetRole in roles)
        {
            if (!HasRequiredScopes(targetRole, grantedScopes))
            {
                missingRoles.Add(targetRole);
                continue;
            }

            var contextId = ResolveContextForSave(
                existingAccounts,
                targetRole,
                userId,
                request.ContextId,
                request.ForceDefaultContext,
                request.PreferDefaultContextWhenEmpty);
            var account = new TwitchAccountSettings();
            flow.ApplyTo(account, now);
            if (!string.IsNullOrWhiteSpace(existingBrowserProfileName))
            {
                account.BrowserProfileName = existingBrowserProfileName;
            }
            entriesToSave.Add((targetRole, contextId, flow.Login, account));
        }

        if (entriesToSave.Count == 0)
        {
            var msg = combined
                ? "Twitch не выдал обязательные права ни для бота, ни для стримера. Изменения не сохранены."
                : $"Twitch не выдал обязательные права для роли «{PlatformAuthRoles.Describe(primaryRole)}». Изменения не сохранены.";
            return new(msg, StatusSeverity.Warning, []);
        }

        var written = accountsStore.UpsertContexts(entriesToSave);
        if (!usedEmbeddedBrowser)
        {
            foreach (var saved in entriesToSave)
            {
                twitchOAuth.ResetRetryState(saved.Role, saved.ContextId);
            }
        }

        var changed = entriesToSave
            .Select(entry => new AuthContextSummary(
                entry.ContextId,
                entry.Role,
                entry.Label,
                userId,
                flow.Login,
                HasToken: true,
                IsDefault: AuthContextIds.IsDefault(entry.ContextId)))
            .ToArray();

        var ownChannel = new ResolvedChannelIdentity(
            new(StreamingPlatform.Twitch, userId),
            flow.Login,
            flow.Login,
            $"https://twitch.tv/{flow.Login}");

        if (!written)
        {
            return new("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning, changed, ownChannel);
        }

        if (!entriesToSave.All(entry => accountsStore.VerifyPersistedAuthorization(entry.Role, entry.ContextId, userId)))
        {
            return new("Не удалось подтвердить запись авторизации Twitch на диске.", StatusSeverity.Warning, changed, ownChannel);
        }

        if (combined && missingRoles.Count > 0)
        {
            var savedRoleText = PlatformAuthRoles.Describe(entriesToSave[0].Role).ToLowerInvariant();
            var missingRoleText = PlatformAuthRoles.Describe(missingRoles[0]).ToLowerInvariant();
            return new(
                $"Аккаунт «{flow.Login}» подключён только как {savedRoleText}: Twitch не выдал обязательные права для роли «{missingRoleText}».",
                StatusSeverity.Warning,
                changed,
                ownChannel);
        }

        var roleText = combined ? "для бота и стримера" : $"как {PlatformAuthRoles.Describe(primaryRole).ToLowerInvariant()}";
        return new($"Аккаунт «{flow.Login}» подключён ({roleText}). Запись проверена.", StatusSeverity.Success, changed, ownChannel);
    }

    public async Task<PlatformTokenValidationResult> ValidateTokenAsync(
        PlatformAuthRole role,
        string accessToken,
        CancellationToken ct = default)
    {
        if (twitchOAuth is null)
        {
            return new("Проверка токена недоступна.", StatusSeverity.Info, false);
        }

        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new("Access Token не задан.", StatusSeverity.Warning, false);
        }

        try
        {
            var info = await twitchOAuth.ValidateAsync(token, ct);
            if (info is null)
            {
                return new("Токен недействителен или просрочен.", StatusSeverity.Error, false);
            }

            var expires = info.ExpiresIn > 0
                ? $" (истекает через {TimeSpan.FromSeconds(info.ExpiresIn):h\\ч\\ m\\м})"
                : string.Empty;
            return new($"Токен действителен: {info.Login}{expires}.", StatusSeverity.Success, true);
        }
        catch (Exception exception)
        {
            logger?.PlatformOAuthValidationFailed(exception, "twitch", role);
            return new($"Не удалось проверить токен. {StreamingErrorMessages.SafeMessage(exception, Platform)}", StatusSeverity.Error, false);
        }
    }

    public async Task<PlatformTokenRefreshResult> RefreshTokenAsync(
        PlatformAuthRole role,
        string contextId,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (twitchOAuth is null)
        {
            return new(false, false, "Обновление токена недоступно.", StatusSeverity.Info);
        }

        var trimmed = refreshToken.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new(false, false, "Refresh Token отсутствует – пройдите вход заново.", StatusSeverity.Warning);
        }

        var settings = settingsManager?.Current.Twitch;
        if (settings is null || string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            return new(false, false, "Заполните и сохраните Client ID и Client Secret во вкладке «Параметры и доступ».", StatusSeverity.Warning);
        }

        try
        {
            var result = await twitchOAuth.RefreshTokenWithResultAsync(role, contextId, settings.ClientId, settings.ClientSecret, trimmed, ct);
            return new(true, result.Persisted, string.Empty, StatusSeverity.Success);
        }
        catch (Exception exception)
        {
            logger?.PlatformOAuthRefreshFailed(exception, "twitch", role);
            return new(false, false, $"Не удалось обновить токен. {StreamingErrorMessages.SafeMessage(exception, Platform)}", StatusSeverity.Error);
        }
    }

    public PlatformManualEditResult SaveManualEdits(PlatformAuthRole role, string contextId, PlatformManualTokenEdits edits)
    {
        var conflict = false;
        var scopes = edits.ScopesEdited ? ParseScopes(edits.ScopesText) : [];
        var (applied, written) = accountsStore.TryMutateExisting(role, contextId, account =>
        {
            if ((edits.AccessTokenEdited && !string.Equals(account.AccessToken ?? string.Empty, edits.InitialAccessToken, StringComparison.Ordinal))
                || (edits.RefreshTokenEdited && !string.Equals(account.RefreshToken ?? string.Empty, edits.InitialRefreshToken, StringComparison.Ordinal)))
            {
                conflict = true;
                return false;
            }

            if (edits.AccessTokenEdited)
            {
                account.AccessToken = edits.AccessToken.Trim();
                account.AccessTokenExpiresAt = null;
                account.StoredScopes = [];
            }

            if (edits.RefreshTokenEdited)
            {
                account.RefreshToken = edits.RefreshToken.Trim();
            }

            if (edits.ScopesEdited && scopes.Length > 0)
            {
                account.Scopes = scopes;
            }

            return edits.AccessTokenEdited || edits.RefreshTokenEdited || (edits.ScopesEdited && scopes.Length > 0);
        });

        if (applied)
        {
            twitchOAuth?.ResetRetryState(role, contextId);
            if (eventBus is not null)
            {
                _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(Platform, role, AuthContextIds.Normalize(contextId)));
            }
        }

        if (conflict)
        {
            return new(false, false, true, "Токен успел обновиться в фоне – данные карточки обновлены, повторите правку при необходимости.", StatusSeverity.Warning);
        }

        if (!applied)
        {
            return new(false, false, false, "Сохранить правки не удалось – роль была удалена или не изменилась.", StatusSeverity.Warning);
        }

        return new(true, written, false, string.Empty, StatusSeverity.Success);
    }

    public async Task<PlatformWebChatSignInResult> SignInWebChatAsync(string accountId, string accountLabel)
    {
        if (webChatLoginDialog is null)
        {
            return new(PlatformWebChatLoginOutcome.Unavailable, null, false, "Встроенный вход в веб-чат недоступен.", StatusSeverity.Warning);
        }

        var result = await webChatLoginDialog.SignInAsync(new(PlatformWebChatDescriptors.Twitch, accountId, accountLabel));
        if (result.Outcome != PlatformWebChatLoginOutcome.Completed || string.IsNullOrWhiteSpace(result.BrowserProfileName))
        {
            return new(result.Outcome, null, false, result.Message,
                result.Outcome == PlatformWebChatLoginOutcome.Failed ? StatusSeverity.Error : StatusSeverity.Info);
        }

        bool written;
        try
        {
            written = accountsStore.SetChatBrowserProfile(accountId, result.BrowserProfileName);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Ошибка записи веб-профиля чата Twitch для {AccountId}", accountId);
            return new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, false,
                "Вход выполнен, но не удалось записать веб-профиль Twitch на диск.", StatusSeverity.Warning);
        }

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(new PlatformAuthorizationRefreshed(Platform, PlatformAuthRole.Bot, AuthContextIds.Default));
        }

        return written
            ? new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, true, $"Веб-чат для «{accountLabel}» подключён.", StatusSeverity.Success)
            : new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, false, "Вход выполнен, но не удалось записать веб-профиль Twitch на диск.", StatusSeverity.Warning);
    }

    public void ResetRetryState(PlatformAuthRole role, string contextId) =>
        twitchOAuth?.ResetRetryState(role, contextId);

    private static string ResolveContextForSave(
        IReadOnlyList<PlatformAccount> accounts,
        PlatformAuthRole role,
        string accountId,
        string? preferredContextId,
        bool forceDefaultContext,
        bool preferDefaultContextWhenEmpty)
    {
        if (forceDefaultContext)
        {
            return AuthContextIds.Default;
        }

        if (!string.IsNullOrWhiteSpace(preferredContextId))
        {
            return AuthContextIds.Normalize(preferredContextId);
        }

        var existing = accounts
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, accountId, StringComparison.Ordinal))
            ?.Authorizations
            .FirstOrDefault(g => g.Role == role && (preferDefaultContextWhenEmpty || !g.IsDefault))
            ?.ContextId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AuthContextIds.Normalize(existing);
        }

        if (preferDefaultContextWhenEmpty)
        {
            var hasAnyDefaultForRole = accounts
                .SelectMany(static a => a.Authorizations)
                .Any(g => g.Role == role && g.IsDefault && g.HasToken);
            if (!hasAnyDefaultForRole)
            {
                return AuthContextIds.Default;
            }
        }

        return $"tw-{role.ToString().ToLowerInvariant()}-{accountId}";
    }

    private static bool HasRequiredScopes(PlatformAuthRole role, IReadOnlyCollection<string> grantedScopes)
    {
        var required = role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired;
        var set = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        return required.All(set.Contains);
    }

    private static string[] ParseScopes(string text) =>
        text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
