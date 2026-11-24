using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public sealed class YouTubeAccountLogin(
    YouTubeSettingsStore? settingsStore,
    YouTubeAccountsStore accountsStore,
    IYouTubeOAuthService? youtubeOAuth,
    IShellLauncher? shellLauncher,
    TimeProvider timeProvider,
    IPlatformWebChatLoginDialog? webChatLoginDialog = null,
    IEventBus? eventBus = null,
    ILogger<YouTubeAccountLogin>? logger = null,
    IEmbeddedYouTubeAuthDialog? embeddedYouTubeAuth = null) : IPlatformAccountLogin
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public bool CanAuthorize => settingsStore is not null && youtubeOAuth is not null && shellLauncher is not null;

    public bool SupportsEmbeddedBrowser => embeddedYouTubeAuth is not null;

    public bool SupportsTokenValidation => true;

    public bool SupportsEditableScopes => false;

    public PlatformWebChatLoginDescriptor? WebChatDescriptor =>
        webChatLoginDialog is not null ? PlatformWebChatDescriptors.YouTube : null;

    public PlatformOAuthMethodDescriptor OAuthMethod => new(
        SupportsEmbeddedBrowser ? "Встроенный или системный браузер" : "Системный браузер",
        SupportsEmbeddedBrowser
            ? "По умолчанию используется встроенное окно – удобно для нескольких аккаунтов, веб-чат привязывается сразу. Если Google отклонит встроенное окно, включите системный браузер."
            : "Вход в аккаунт откроется в системном браузере с выбором аккаунта Google.",
        SupportsEmbeddedBrowser,
        SupportsSystemBrowser: true);

    public IReadOnlyList<string> GetDefaultScopes(PlatformAuthRole role) =>
        [.. YouTubeScopes.RequiredFor(role)];

    public bool IsScopeSatisfied(IReadOnlySet<string> grantedScopes, string requiredScope) =>
        YouTubeScopes.IsScopeSatisfied(grantedScopes, requiredScope);

    public bool TryEvaluateScopeMismatch(
        PlatformAuthRole role,
        string[] expectedScopes,
        string[] storedScopes,
        out string[] effectiveExpectedScopes)
    {
        var missingRequired = storedScopes.Length > 0 && !YouTubeScopes.ContainsRequiredScopes(role, storedScopes);
        effectiveExpectedScopes = missingRequired ? [.. YouTubeScopes.RequiredFor(role)] : expectedScopes;
        return missingRequired
            || (storedScopes.Length > 0 && !new HashSet<string>(storedScopes, StringComparer.Ordinal).SetEquals(effectiveExpectedScopes));
    }

    public bool ShouldTreatScopeMismatchAsSessionFailure(PlatformAuthRole role, IReadOnlySet<string> grantedScopes) =>
        !(role == PlatformAuthRole.Broadcaster && YouTubeScopes.ContainsUsableScopes(role, grantedScopes));

    public async Task<PlatformAccountLoginResult> AuthorizeAsync(
        PlatformAccountLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (settingsStore is null || youtubeOAuth is null || shellLauncher is null)
        {
            return new("Авторизация YouTube недоступна.", StatusSeverity.Warning, []);
        }

        var settings = settingsStore.Load();
        var secret = accountsStore.LoadClientSecret();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            return new("Заполните и сохраните ключи во вкладке «Параметры и доступ».", StatusSeverity.Warning, []);
        }

        var roles = request.Roles.Count > 0 ? request.Roles : [PlatformAuthRole.Bot];
        var combined = roles.Count > 1;
        var primaryRole = roles[0];

        string[] scopes = combined
            ? [.. YouTubeScopes.CombinedDefault]
            : request.Scopes.Count > 0
                ? [.. request.Scopes]
                : [.. YouTubeScopes.RequiredFor(primaryRole)];

        YouTubeOAuthFlowResult? flow = null;
        if (!request.UseSystemBrowser && embeddedYouTubeAuth is not null)
        {
            var embedded = await embeddedYouTubeAuth.AuthorizeAsync(
                new(primaryRole, settings.ClientId, secret, scopes, combined),
                cancellationToken);
            if (embedded.Outcome != EmbeddedYouTubeAuthOutcome.Completed || embedded.Flow is null)
            {
                var severity = embedded.Outcome switch
                {
                    EmbeddedYouTubeAuthOutcome.Failed => StatusSeverity.Error,
                    EmbeddedYouTubeAuthOutcome.Unavailable => StatusSeverity.Warning,
                    _ => StatusSeverity.Info,
                };
                return new(embedded.Message, severity, []);
            }

            flow = embedded.Flow;
        }

        flow ??= await youtubeOAuth.StartOAuthFlowToDraftAsync(
            primaryRole,
            settings.ClientId,
            secret,
            scopes,
            url => shellLauncher.Open(url),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(flow.ChannelId))
        {
            return new("YouTube не сообщил ID авторизованного канала.", StatusSeverity.Error, []);
        }

        var id = flow.ChannelId.Trim();
        var name = string.IsNullOrWhiteSpace(flow.ChannelTitle) ? id : flow.ChannelTitle;
        if (!combined && !string.IsNullOrWhiteSpace(request.AccountId)
            && !string.Equals(request.AccountId, id, StringComparison.Ordinal))
        {
            return new(
                $"Вы вошли в другой аккаунт («{name}»), а права запрашивались для другого аккаунта. Изменения не сохранены.",
                StatusSeverity.Warning,
                []);
        }

        var now = timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        var existingAccounts = accountsStore.ListAccounts();
        var existingBrowserProfileName = existingAccounts
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, id, StringComparison.Ordinal))
            ?.Authorizations
            .Select(static g => g.ChatBrowserProfile?.ProfileName)
            .FirstOrDefault(static profile => !string.IsNullOrWhiteSpace(profile));
        List<(PlatformAuthRole Role, string ContextId, string Label, YouTubeAccountSettings Account)> entriesToSave = [];
        List<PlatformAuthRole> missingRoles = [];

        foreach (var targetRole in roles)
        {
            if (!YouTubeScopes.ContainsRequiredScopes(targetRole, grantedScopes))
            {
                missingRoles.Add(targetRole);
                continue;
            }

            var contextId = ResolveContextForSave(
                existingAccounts,
                targetRole,
                id,
                request.ContextId,
                request.ForceDefaultContext,
                request.PreferDefaultContextWhenEmpty);
            var account = new YouTubeAccountSettings();
            flow.ApplyTo(account, now);
            if (string.IsNullOrWhiteSpace(account.BrowserProfileName) && !string.IsNullOrWhiteSpace(existingBrowserProfileName))
            {
                account.BrowserProfileName = existingBrowserProfileName;
            }
            entriesToSave.Add((targetRole, contextId, name, account));
        }

        if (entriesToSave.Count == 0)
        {
            var msg = combined
                ? "YouTube не выдал обязательные права ни для бота, ни для стримера. Изменения не сохранены."
                : $"YouTube не выдал обязательные права для роли «{PlatformAuthRoles.Describe(primaryRole)}». Изменения не сохранены.";
            return new(msg, StatusSeverity.Warning, []);
        }

        var written = accountsStore.UpsertContexts(entriesToSave);
        foreach (var saved in entriesToSave)
        {
            youtubeOAuth.ResetRetryState(saved.Role, saved.ContextId);
        }

        var changed = entriesToSave
            .Select(entry => new AuthContextSummary(
                entry.ContextId,
                entry.Role,
                entry.Label,
                id,
                name,
                HasToken: true,
                IsDefault: AuthContextIds.IsDefault(entry.ContextId)))
            .ToArray();

        var ownChannel = new ResolvedChannelIdentity(
            new(StreamingPlatform.YouTube, id),
            name,
            id,
            $"https://youtube.com/channel/{id}");

        if (!written)
        {
            return new("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning, changed, ownChannel);
        }

        if (!entriesToSave.All(entry => accountsStore.VerifyPersistedAuthorization(entry.Role, entry.ContextId, id)))
        {
            return new("Не удалось подтвердить запись авторизации YouTube на диске.", StatusSeverity.Warning, changed, ownChannel);
        }

        if (combined && missingRoles.Count > 0)
        {
            var savedRoleText = PlatformAuthRoles.Describe(entriesToSave[0].Role).ToLowerInvariant();
            var missingRoleText = PlatformAuthRoles.Describe(missingRoles[0]).ToLowerInvariant();
            return new(
                $"Аккаунт «{name}» подключён только как {savedRoleText}: YouTube не выдал обязательные права для роли «{missingRoleText}».",
                StatusSeverity.Warning,
                changed,
                ownChannel);
        }

        var roleText = combined ? "для бота и стримера" : $"как {PlatformAuthRoles.Describe(primaryRole).ToLowerInvariant()}";
        return new($"Аккаунт «{name}» подключён ({roleText}). Запись проверена.", StatusSeverity.Success, changed, ownChannel);
    }

    public async Task<PlatformTokenValidationResult> ValidateTokenAsync(
        PlatformAuthRole role,
        string accessToken,
        CancellationToken ct = default)
    {
        if (youtubeOAuth is null)
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
            var info = await youtubeOAuth.ValidateAsync(token, ct);
            if (info is null)
            {
                return new("Токен недействителен или просрочен.", StatusSeverity.Error, false);
            }

            var subject = !string.IsNullOrWhiteSpace(info.ChannelTitle)
                ? $": {info.ChannelTitle}"
                : !string.IsNullOrWhiteSpace(info.ChannelId)
                    ? $": {info.ChannelId}"
                    : string.Empty;
            var expires = info.ExpiresInSeconds > 0
                ? $" (истекает через {TimeSpan.FromSeconds(info.ExpiresInSeconds):h\\ч\\ m\\м})"
                : string.Empty;
            return new($"Токен действителен{subject}{expires}.", StatusSeverity.Success, true);
        }
        catch (Exception exception)
        {
            logger?.PlatformOAuthValidationFailed(exception, "youtube", role);
            return new($"Не удалось проверить токен. {StreamingErrorMessages.SafeMessage(exception, Platform)}", StatusSeverity.Error, false);
        }
    }

    public async Task<PlatformTokenRefreshResult> RefreshTokenAsync(
        PlatformAuthRole role,
        string contextId,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (settingsStore is null || youtubeOAuth is null)
        {
            return new(false, false, "Обновление токена недоступно.", StatusSeverity.Info);
        }

        var trimmed = refreshToken.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new(false, false, "Refresh Token отсутствует – пройдите вход заново.", StatusSeverity.Warning);
        }

        var settings = settingsStore.Load();
        var secret = accountsStore.LoadClientSecret();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            return new(false, false, "Заполните и сохраните реквизиты Google-проекта во вкладке «Параметры и доступ».", StatusSeverity.Warning);
        }

        try
        {
            var result = await youtubeOAuth.RefreshTokenWithResultAsync(role, contextId, settings.ClientId, secret, trimmed, ct);
            return new(true, result.Persisted, string.Empty, StatusSeverity.Success);
        }
        catch (Exception exception)
        {
            logger?.PlatformOAuthRefreshFailed(exception, "youtube", role);
            return new(false, false, $"Не удалось обновить токен. {StreamingErrorMessages.SafeMessage(exception, Platform)}", StatusSeverity.Error);
        }
    }

    public PlatformManualEditResult SaveManualEdits(PlatformAuthRole role, string contextId, PlatformManualTokenEdits edits)
    {
        var conflict = false;
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

            return edits.AccessTokenEdited || edits.RefreshTokenEdited;
        });

        if (applied)
        {
            youtubeOAuth?.ResetRetryState(role, contextId);
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

        var result = await webChatLoginDialog.SignInAsync(new(PlatformWebChatDescriptors.YouTube, accountId, accountLabel));
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
            logger?.LogWarning(exception, "Ошибка записи веб-профиля чата YouTube для {AccountId}", accountId);
            return new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, false,
                "Вход выполнен, но не удалось записать веб-профиль YouTube на диск.", StatusSeverity.Warning);
        }

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(new PlatformAuthorizationRefreshed(Platform, PlatformAuthRole.Bot, AuthContextIds.Default));
        }

        return written
            ? new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, true, $"Веб-чат для «{accountLabel}» подключён.", StatusSeverity.Success)
            : new(PlatformWebChatLoginOutcome.Completed, result.BrowserProfileName, false, "Вход выполнен, но не удалось записать веб-профиль YouTube на диск.", StatusSeverity.Warning);
    }

    public void ResetRetryState(PlatformAuthRole role, string contextId) =>
        youtubeOAuth?.ResetRetryState(role, contextId);

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

        return $"yt-{role.ToString().ToLowerInvariant()}-{accountId}";
    }
}
