using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.Services.Platform;
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
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class PlatformAccountsSectionViewModel : ObservableObject
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly AccountsStore? _twitchAccountsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly ITwitchOAuthService? _twitchOAuth;
    private readonly IYouTubeOAuthService? _youtubeOAuth;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly IShellLauncher? _shellLauncher;
    private readonly IDialogService? _dialogService;
    private readonly IEventBus? _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IEmbeddedTwitchAuthDialog? _embeddedTwitchAuth;
    private readonly ILogger<PlatformAccountsSectionViewModel>? _logger;
    private readonly IChatPlatformRegistry? _platformRegistry;

    private OAuthSettingsViewModel? _twitchCredentials;
    private YouTubeSettingsSectionViewModel? _youtubeCredentials;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformDisplayName), nameof(AccountsTitle), nameof(SupportsAccounts),
        nameof(CanAuthorize), nameof(SupportsBrowserToggle))]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeCombinedCommand), nameof(AuthorizeBotCommand), nameof(AuthorizeBroadcasterCommand))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeCombinedCommand), nameof(AuthorizeBotCommand), nameof(AuthorizeBroadcasterCommand))]
    private bool _isAuthorizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TechnicalModeButtonLabel))]
    private bool _isTechnicalMode;

    [ObservableProperty]
    private bool _useSystemBrowser;

    [ObservableProperty]
    private string? _statusNotice;

    [ObservableProperty]
    private StatusSeverity _statusNoticeSeverity;

    public PlatformAccountsSectionViewModel(
        ChannelCatalogStore catalogStore,
        AccountsStore? twitchAccountsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        ITwitchOAuthService? twitchOAuth = null,
        IYouTubeOAuthService? youtubeOAuth = null,
        SettingsManager? settingsManager = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IShellLauncher? shellLauncher = null,
        IDialogService? dialogService = null,
        IEventBus? eventBus = null,
        TimeProvider? timeProvider = null,
        IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
        ILogger<PlatformAccountsSectionViewModel>? logger = null,
        IChatPlatformRegistry? platformRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        _catalogStore = catalogStore;
        _twitchAccountsStore = twitchAccountsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _twitchOAuth = twitchOAuth;
        _youtubeOAuth = youtubeOAuth;
        _settingsManager = settingsManager;
        _youtubeSettingsStore = youtubeSettingsStore;
        _shellLauncher = shellLauncher;
        _dialogService = dialogService;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _embeddedTwitchAuth = embeddedTwitchAuth;
        _logger = logger;
        _platformRegistry = platformRegistry;
    }

    public event EventHandler<StreamingPlatform>? AccountsChanged;

    public ObservableCollection<PlatformAccountCardViewModel> Accounts { get; } = [];

    public int AccountCount => Accounts.Count;

    public bool HasAccounts => Accounts.Count > 0;

    public string PlatformDisplayName =>
        _platformRegistry?.Find(SelectedPlatform)?.DisplayName ?? PlatformVisuals.GetDisplayName(SelectedPlatform);

    public string AccountsTitle => $"Аккаунты {PlatformDisplayName}";

    public string TechnicalModeButtonLabel => IsTechnicalMode ? "Обычный вид" : "Технический вид";

    public bool SupportsAccounts => SupportsAccountsFor(SelectedPlatform);

    public bool SupportsBrowserToggle => SelectedPlatform == StreamingPlatform.Twitch && _embeddedTwitchAuth is not null;

    public bool CanAuthorize => CanAuthorizePlatform(SelectedPlatform);

    internal void SetCredentialDrafts(OAuthSettingsViewModel? twitch, YouTubeSettingsSectionViewModel? youtube)
    {
        _twitchCredentials = twitch;
        _youtubeCredentials = youtube;
    }

    public bool SupportsAccountsFor(StreamingPlatform? platform) => platform switch
    {
        StreamingPlatform.Twitch => _twitchAccountsStore is not null,
        StreamingPlatform.YouTube => _youtubeAccountsStore is not null,
        _ => false,
    };

    private bool CanAuthorizePlatform(StreamingPlatform platform) =>
        _shellLauncher is not null
        && ((platform == StreamingPlatform.Twitch && _twitchOAuth is not null && _twitchAccountsStore is not null && _settingsManager is not null)
            || (platform == StreamingPlatform.YouTube && _youtubeOAuth is not null && _youtubeAccountsStore is not null && _youtubeSettingsStore is not null));

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        StatusNotice = null;
        Load();
    }

    public void Load()
    {
        var expandedAccounts = Accounts
            .Where(static card => card.IsScopesExpanded)
            .Select(static card => card.AccountId)
            .ToHashSet(StringComparer.Ordinal);

        Accounts.Clear();
        if (!SupportsAccounts)
        {
            NotifyCountChanged();
            return;
        }

        var platform = SelectedPlatform;
        var platformAccounts = ListPlatformAccounts(platform);
        var channels = _catalogStore.Load().Channels
            .Where(c => c.Platform == platform && c.IsConfigured)
            .ToArray();
        var now = _timeProvider.GetUtcNow();
        var canAuth = CanAuthorizePlatform(platform);

        foreach (var account in platformAccounts)
        {
            var grants = BuildGrantViewModels(platform, account, now, canAuth);
            var assignedSummary = BuildAssignedChannelsSummary(account.Key.AccountId, channels);
            var card = new PlatformAccountCardViewModel(
                platform,
                account.Key.AccountId,
                account.DisplayName,
                assignedSummary,
                grants,
                canAuth,
                AddRoleForAccountAsync,
                SignOutAccountAsync)
            {
                IsScopesExpanded = expandedAccounts.Contains(account.Key.AccountId),
            };
            Accounts.Add(card);
        }

        NotifyCountChanged();
    }

    internal void RefreshIfPlatform(StreamingPlatform platform)
    {
        if (SelectedPlatform == platform)
        {
            Load();
        }
    }

    private void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(AccountCount));
        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    private void ToggleTechnicalMode() => IsTechnicalMode = !IsTechnicalMode;

    private bool CanStartAuthorization() => !IsAuthorizing && CanAuthorize;

    [RelayCommand(CanExecute = nameof(CanStartAuthorization))]
    private Task AuthorizeCombinedAsync() => AuthorizePlatformAsync(SelectedPlatform, combined: true, role: PlatformAuthRole.Bot, targetAccountId: null);

    [RelayCommand(CanExecute = nameof(CanStartAuthorization))]
    private Task AuthorizeBotAsync() => AuthorizePlatformAsync(SelectedPlatform, combined: false, role: PlatformAuthRole.Bot, targetAccountId: null);

    [RelayCommand(CanExecute = nameof(CanStartAuthorization))]
    private Task AuthorizeBroadcasterAsync() => AuthorizePlatformAsync(SelectedPlatform, combined: false, role: PlatformAuthRole.Broadcaster, targetAccountId: null);

    private Task AddRoleForAccountAsync(PlatformAccountCardViewModel card, PlatformAuthRole role) =>
        AuthorizePlatformAsync(card.Platform, combined: false, role: role, targetAccountId: card.AccountId);

    private Task ReauthorizeGrantAsync(PlatformAccountGrantViewModel grant) =>
        AuthorizePlatformAsync(grant.Platform, combined: false, role: grant.Role, targetAccountId: grant.AccountId, preferredContextId: grant.ContextId, customScopes: grant.GetRequestedScopes());

    private async Task AuthorizePlatformAsync(
        StreamingPlatform platform,
        bool combined,
        PlatformAuthRole role,
        string? targetAccountId,
        string? preferredContextId = null,
        string[]? customScopes = null)
    {
        if (IsAuthorizing)
        {
            SetNotice("Дождитесь завершения текущего входа.", StatusSeverity.Info);
            return;
        }

        IsAuthorizing = true;
        SetNotice("Ожидаем вход на площадке…", StatusSeverity.Info);
        if (combined)
        {
            _logger?.CombinedAccountAuthorizationStarted(platform);
        }
        else
        {
            _logger?.ChannelAuthorizationStarted(platform, role);
        }

        try
        {
            var succeeded = platform switch
            {
                StreamingPlatform.Twitch => await AuthorizeTwitchAsync(combined, role, targetAccountId, preferredContextId, customScopes),
                StreamingPlatform.YouTube => await AuthorizeYouTubeAsync(combined, role, targetAccountId, preferredContextId),
                _ => false,
            };

            if (succeeded)
            {
                Load();
                AccountsChanged?.Invoke(this, platform);
            }
        }
        catch (OperationCanceledException)
        {
            SetNotice("Вход отменён.", StatusSeverity.Info);
        }
        catch (Exception exception)
        {
            _logger?.ChannelAuthorizationFailed(platform, role, exception.GetType().Name);
            SetNotice($"Не удалось подключить аккаунт. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    private async Task<bool> AuthorizeTwitchAsync(
        bool combined,
        PlatformAuthRole role,
        string? targetAccountId,
        string? preferredContextId,
        string[]? customScopes)
    {
        if (_settingsManager is null || _twitchOAuth is null || _twitchAccountsStore is null || _shellLauncher is null)
        {
            return false;
        }

        var settings = _settingsManager.Current.Twitch;
        if (_twitchCredentials is { } twitch
            && (twitch.ClientId.Trim() != settings.ClientId
                || twitch.ClientSecret.Trim() != settings.ClientSecret
                || twitch.RedirectUri.Trim() != settings.RedirectUri))
        {
            SetNotice("Сначала сохраните новые реквизиты во вкладке «Параметры и доступ», затем повторите вход.", StatusSeverity.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            SetNotice("Заполните и сохраните ключи во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return false;
        }

        string[] scopes = combined
            ? [.. TwitchScopes.CombinedDefault]
            : customScopes is { Length: > 0 }
                ? customScopes
                : role == PlatformAuthRole.Bot
                    ? [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional]
                    : [.. TwitchScopes.BroadcasterRequired];

        OAuthFlowResult? flow = null;
        if (!UseSystemBrowser && _embeddedTwitchAuth is not null)
        {
            var embedded = await _embeddedTwitchAuth.AuthorizeAsync(new(role, settings.ClientId, settings.ClientSecret,
                scopes, settings.RedirectUri, CheckBroadcasterChannel: false));
            if (embedded.Outcome is EmbeddedTwitchAuthOutcome.Canceled or EmbeddedTwitchAuthOutcome.Failed)
            {
                SetNotice(embedded.Message, embedded.Outcome == EmbeddedTwitchAuthOutcome.Failed ? StatusSeverity.Error : StatusSeverity.Info);
                return false;
            }

            flow = embedded.Flow;
        }

        flow ??= await _twitchOAuth.StartOAuthFlowToDraftAsync(role, settings.ClientId, settings.ClientSecret, scopes,
            settings.RedirectUri, url => _shellLauncher.Open(url), checkBroadcasterChannel: false);

        if (string.IsNullOrWhiteSpace(flow.UserId))
        {
            SetNotice("Twitch не сообщил ID аккаунта.", StatusSeverity.Error);
            return false;
        }

        var id = flow.UserId.Trim();
        var now = _timeProvider.GetUtcNow();
        var written = true;
        List<(PlatformAuthRole Role, string ContextId)> savedContexts = [];

        if (combined)
        {
            foreach (var targetRole in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
            {
                var contextId = ResolveContextForSave(StreamingPlatform.Twitch, targetRole, id, preferredContextId: null);
                var account = new TwitchAccountSettings();
                flow.ApplyTo(account, now);
                written &= _twitchAccountsStore.UpsertContext(targetRole, contextId, flow.Login, account);
                savedContexts.Add((targetRole, contextId));
            }
        }
        else
        {
            var sameAccount = string.Equals(targetAccountId, id, StringComparison.Ordinal);
            var contextId = ResolveContextForSave(StreamingPlatform.Twitch, role, id, sameAccount ? preferredContextId : null);
            var account = new TwitchAccountSettings();
            flow.ApplyTo(account, now);
            written = _twitchAccountsStore.UpsertContext(role, contextId, flow.Login, account);
            savedContexts.Add((role, contextId));
        }

        foreach (var saved in savedContexts)
        {
            _twitchOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.Twitch, saved.Role, saved.ContextId);
        }

        if (!written)
        {
            SetNotice("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning);
            return true;
        }

        var roleText = combined ? "для бота и стримера" : $"как {PlatformAuthRoles.Describe(role).ToLowerInvariant()}";
        SetNotice($"Аккаунт «{flow.Login}» подключён ({roleText}).", StatusSeverity.Success);
        return true;
    }

    private async Task<bool> AuthorizeYouTubeAsync(
        bool combined,
        PlatformAuthRole role,
        string? targetAccountId,
        string? preferredContextId)
    {
        if (_youtubeSettingsStore is null || _youtubeOAuth is null || _youtubeAccountsStore is null || _shellLauncher is null)
        {
            return false;
        }

        var settings = _youtubeSettingsStore.Load();
        var secret = _youtubeAccountsStore.LoadClientSecret();
        if (_youtubeCredentials is { } youtube
            && (youtube.ClientId.Trim() != settings.ClientId || youtube.ClientSecret.Trim() != secret))
        {
            SetNotice("Сначала сохраните новые реквизиты во вкладке «Параметры и доступ», затем повторите вход.", StatusSeverity.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            SetNotice("Заполните и сохраните ключи во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return false;
        }

        string[] scopes = combined
            ? [.. YouTubeScopes.CombinedDefault]
            : [.. YouTubeScopes.RequiredFor(role)];

        var flow = await _youtubeOAuth.StartOAuthFlowToDraftAsync(role, settings.ClientId, secret, scopes,
            url => _shellLauncher.Open(url));
        if (string.IsNullOrWhiteSpace(flow.ChannelId))
        {
            SetNotice("YouTube не сообщил ID авторизованного канала.", StatusSeverity.Error);
            return false;
        }

        var id = flow.ChannelId.Trim();
        var name = string.IsNullOrWhiteSpace(flow.ChannelTitle) ? id : flow.ChannelTitle;
        var now = _timeProvider.GetUtcNow();
        var written = true;
        List<(PlatformAuthRole Role, string ContextId)> savedContexts = [];

        if (combined)
        {
            foreach (var targetRole in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
            {
                var contextId = ResolveContextForSave(StreamingPlatform.YouTube, targetRole, id, preferredContextId: null);
                var account = new YouTubeAccountSettings();
                flow.ApplyTo(account, now);
                written &= _youtubeAccountsStore.UpsertContext(targetRole, contextId, name, account);
                savedContexts.Add((targetRole, contextId));
            }
        }
        else
        {
            var sameAccount = string.Equals(targetAccountId, id, StringComparison.Ordinal);
            var contextId = ResolveContextForSave(StreamingPlatform.YouTube, role, id, sameAccount ? preferredContextId : null);
            var account = new YouTubeAccountSettings();
            flow.ApplyTo(account, now);
            written = _youtubeAccountsStore.UpsertContext(role, contextId, name, account);
            savedContexts.Add((role, contextId));
        }

        foreach (var saved in savedContexts)
        {
            _youtubeOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.YouTube, saved.Role, saved.ContextId);
        }

        if (!written)
        {
            SetNotice("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning);
            return true;
        }

        var roleText = combined ? "для бота и стримера" : $"как {PlatformAuthRoles.Describe(role).ToLowerInvariant()}";
        SetNotice($"Аккаунт «{name}» подключён ({roleText}).", StatusSeverity.Success);
        return true;
    }

    private string ResolveContextForSave(StreamingPlatform platform, PlatformAuthRole role, string accountId, string? preferredContextId)
    {
        if (!string.IsNullOrWhiteSpace(preferredContextId))
        {
            return AuthContextIds.Normalize(preferredContextId);
        }

        var existing = ListPlatformAccounts(platform)
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, accountId, StringComparison.Ordinal))
            ?.Authorizations
            .FirstOrDefault(g => g.Role == role)
            ?.ContextId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AuthContextIds.Normalize(existing);
        }

        var prefix = platform == StreamingPlatform.Twitch ? "tw" : "yt";
        return $"{prefix}-{role.ToString().ToLowerInvariant()}-{accountId}";
    }

    private Task SignOutAccountAsync(PlatformAccountCardViewModel card)
    {
        var prompt = string.IsNullOrWhiteSpace(card.AssignedChannelsText) || card.AssignedChannelsText.StartsWith("Не назначен", StringComparison.Ordinal)
            ? $"Удалить токены и выйти из аккаунта «{card.DisplayName}»?"
            : $"Удалить токены и выйти из аккаунта «{card.DisplayName}»?\n\n{card.AssignedChannelsText}. После выхода на этих каналах нужно будет выбрать другой аккаунт или снять назначение.";

        if (_dialogService is not null && !_dialogService.Confirm("Выход из аккаунта", prompt))
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<(PlatformAuthRole Role, string ContextId)> removed;
        bool written;
        switch (card.Platform)
        {
            case StreamingPlatform.Twitch when _twitchAccountsStore is not null:
                (removed, written) = _twitchAccountsStore.RemoveAccount(card.AccountId);
                foreach (var entry in removed)
                {
                    _twitchOAuth?.ResetRetryState(entry.Role, entry.ContextId);
                    PublishAuthorizationRefreshed(StreamingPlatform.Twitch, entry.Role, entry.ContextId);
                }
                break;
            case StreamingPlatform.YouTube when _youtubeAccountsStore is not null:
                (removed, written) = _youtubeAccountsStore.RemoveAccount(card.AccountId);
                foreach (var entry in removed)
                {
                    _youtubeOAuth?.ResetRetryState(entry.Role, entry.ContextId);
                    PublishAuthorizationRefreshed(StreamingPlatform.YouTube, entry.Role, entry.ContextId);
                }
                break;
            default:
                return Task.CompletedTask;
        }

        _logger?.PlatformAccountSignedOut(card.Platform, card.AccountId);
        Load();
        AccountsChanged?.Invoke(this, card.Platform);
        SetNotice(written
            ? $"Аккаунт «{card.DisplayName}» удалён."
            : $"Аккаунт «{card.DisplayName}» очищён в памяти до перезапуска.",
            written ? StatusSeverity.Info : StatusSeverity.Warning);
        return Task.CompletedTask;
    }

    private Task RemoveGrantRoleAsync(PlatformAccountGrantViewModel grant)
    {
        if (_dialogService is not null
            && !_dialogService.Confirm("Удаление роли аккаунта",
                $"Удалить права роли «{grant.RoleTitle}» у аккаунта «{grant.AccountDisplayName}»?"))
        {
            return Task.CompletedTask;
        }

        bool changed;
        bool written;
        switch (grant.Platform)
        {
            case StreamingPlatform.Twitch when _twitchAccountsStore is not null:
                (changed, written) = _twitchAccountsStore.RemoveRoleContext(grant.Role, grant.ContextId);
                _twitchOAuth?.ResetRetryState(grant.Role, grant.ContextId);
                PublishAuthorizationRefreshed(StreamingPlatform.Twitch, grant.Role, grant.ContextId);
                break;
            case StreamingPlatform.YouTube when _youtubeAccountsStore is not null:
                (changed, written) = _youtubeAccountsStore.RemoveRoleContext(grant.Role, grant.ContextId);
                _youtubeOAuth?.ResetRetryState(grant.Role, grant.ContextId);
                PublishAuthorizationRefreshed(StreamingPlatform.YouTube, grant.Role, grant.ContextId);
                break;
            default:
                return Task.CompletedTask;
        }

        if (changed)
        {
            _logger?.PlatformAccountRoleRemoved(grant.Platform, grant.Role, grant.AccountId);
            Load();
            AccountsChanged?.Invoke(this, grant.Platform);
            SetNotice(written
                ? $"Роль «{grant.RoleTitle}» удалена у аккаунта «{grant.AccountDisplayName}»."
                : "Изменение принято в памяти до перезапуска.",
                written ? StatusSeverity.Info : StatusSeverity.Warning);
        }

        return Task.CompletedTask;
    }

    private async Task ValidateGrantAsync(PlatformAccountGrantViewModel grant)
    {
        if (grant.Platform != StreamingPlatform.Twitch || _twitchOAuth is null)
        {
            return;
        }

        var token = grant.AccessTokenValue.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            grant.SetActionStatus("Access Token не задан.", StatusSeverity.Warning);
            return;
        }

        grant.SetActionStatus("Проверка токена…", StatusSeverity.Info);
        try
        {
            var info = await _twitchOAuth.ValidateAsync(token, CancellationToken.None);
            if (info is null)
            {
                grant.SetActionStatus("Токен недействителен или просрочен.", StatusSeverity.Error);
                return;
            }

            var expires = info.ExpiresIn > 0
                ? $" (истекает через {TimeSpan.FromSeconds(info.ExpiresIn):h\\ч\\ m\\м})"
                : string.Empty;
            grant.SetActionStatus($"Токен действителен: {info.Login}{expires}.", StatusSeverity.Success);
        }
        catch (Exception exception)
        {
            _logger?.PlatformOAuthValidationFailed(exception, "twitch", grant.Role);
            grant.SetActionStatus($"Не удалось проверить токен. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
        }
    }

    private async Task RefreshGrantAsync(PlatformAccountGrantViewModel grant)
    {
        var refreshToken = grant.RefreshTokenValue.Trim();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            grant.SetActionStatus("Refresh Token отсутствует – пройдите вход заново.", StatusSeverity.Warning);
            return;
        }

        grant.SetActionStatus("Обновление токена…", StatusSeverity.Info);
        try
        {
            if (grant.Platform == StreamingPlatform.Twitch)
            {
                if (_twitchOAuth is null || _settingsManager is null)
                {
                    return;
                }

                var settings = _settingsManager.Current.Twitch;
                if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
                {
                    grant.SetActionStatus("Заполните и сохраните Client ID и Client Secret во вкладке «Параметры и доступ».", StatusSeverity.Warning);
                    return;
                }

                await _twitchOAuth.RefreshTokenAsync(grant.Role, grant.ContextId, settings.ClientId, settings.ClientSecret, refreshToken, CancellationToken.None);
            }
            else if (grant.Platform == StreamingPlatform.YouTube)
            {
                if (_youtubeOAuth is null || _youtubeSettingsStore is null || _youtubeAccountsStore is null)
                {
                    return;
                }

                var settings = _youtubeSettingsStore.Load();
                var secret = _youtubeAccountsStore.LoadClientSecret();
                if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
                {
                    grant.SetActionStatus("Заполните и сохраните реквизиты Google-проекта во вкладке «Параметры и доступ».", StatusSeverity.Warning);
                    return;
                }

                await _youtubeOAuth.RefreshTokenAsync(grant.Role, grant.ContextId, settings.ClientId, secret, refreshToken, CancellationToken.None);
            }

            var refreshedAt = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZoneInfo.Local)
                .ToString("dd.MM.yyyy HH:mm:ss", UiCulture.Russian);
            Load();
            AccountsChanged?.Invoke(this, grant.Platform);
            SetNotice($"Токен ({grant.RoleTitle}, «{grant.AccountDisplayName}») обновлён ({refreshedAt}).", StatusSeverity.Success);
        }
        catch (Exception exception)
        {
            _logger?.PlatformOAuthRefreshFailed(exception, grant.Platform.ToString().ToLowerInvariant(), grant.Role);
            grant.SetActionStatus($"Не удалось обновить токен. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
        }
    }

    private Task SaveManualGrantEditsAsync(PlatformAccountGrantViewModel grant)
    {
        var written = false;
        if (grant.Platform == StreamingPlatform.Twitch && _twitchAccountsStore is not null)
        {
            var scopes = ParseScopes(grant.ScopesText);
            written = _twitchAccountsStore.Mutate(grant.Role, grant.ContextId, account =>
            {
                account.AccessToken = grant.AccessTokenValue.Trim();
                account.RefreshToken = grant.RefreshTokenValue.Trim();
                if (scopes.Length > 0)
                {
                    account.Scopes = scopes;
                }
            });
            _twitchOAuth?.ResetRetryState(grant.Role, grant.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.Twitch, grant.Role, grant.ContextId);
        }
        else if (grant.Platform == StreamingPlatform.YouTube && _youtubeAccountsStore is not null)
        {
            written = _youtubeAccountsStore.Mutate(grant.Role, grant.ContextId, account =>
            {
                account.AccessToken = grant.AccessTokenValue.Trim();
                account.RefreshToken = grant.RefreshTokenValue.Trim();
            });
            _youtubeOAuth?.ResetRetryState(grant.Role, grant.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.YouTube, grant.Role, grant.ContextId);
        }

        Load();
        AccountsChanged?.Invoke(this, grant.Platform);
        SetNotice(written
            ? $"Правки токена ({grant.RoleTitle}, «{grant.AccountDisplayName}») сохранены."
            : "Правки приняты в памяти до перезапуска.",
            written ? StatusSeverity.Success : StatusSeverity.Warning);
        return Task.CompletedTask;
    }

    private IReadOnlyList<PlatformAccount> ListPlatformAccounts(StreamingPlatform platform) => platform switch
    {
        StreamingPlatform.Twitch => _twitchAccountsStore?.ListAccounts() ?? [],
        StreamingPlatform.YouTube => _youtubeAccountsStore?.ListAccounts() ?? [],
        _ => [],
    };

    private IReadOnlyList<PlatformAccountGrantViewModel> BuildGrantViewModels(
        StreamingPlatform platform,
        PlatformAccount account,
        DateTimeOffset now,
        bool canAuthorize)
    {
        var grants = new List<PlatformAccountGrantViewModel>();
        foreach (var role in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
        {
            var roleAuthorizations = account.Authorizations
                .Where(a => a.Role == role)
                .OrderByDescending(static a => a.HasToken)
                .ThenBy(static a => a.IsDefault)
                .ToArray();
            var summary = roleAuthorizations.FirstOrDefault();
            if (summary is null)
            {
                continue;
            }

            if (platform == StreamingPlatform.Twitch && _twitchAccountsStore is not null)
            {
                var settings = _twitchAccountsStore.Load(role, summary.ContextId);
                grants.Add(BuildGrantViewModel(platform, account.Key.AccountId, account.DisplayName, role, summary.ContextId, summary.IsDefault, settings, now, canAuthorize));
            }
            else if (platform == StreamingPlatform.YouTube && _youtubeAccountsStore is not null)
            {
                var settings = _youtubeAccountsStore.Load(role, summary.ContextId);
                grants.Add(BuildGrantViewModel(platform, account.Key.AccountId, account.DisplayName, role, summary.ContextId, summary.IsDefault, settings, now, canAuthorize));
            }
        }

        return grants;
    }

    private PlatformAccountGrantViewModel BuildGrantViewModel(
        StreamingPlatform platform,
        string accountId,
        string accountDisplayName,
        PlatformAuthRole role,
        string contextId,
        bool isDefault,
        IRoleAccountSettings settings,
        DateTimeOffset now,
        bool canAuthorize)
    {
        var hasAccess = !string.IsNullOrWhiteSpace(settings.AccessToken);
        var hasRefresh = !string.IsNullOrWhiteSpace(settings.RefreshToken);
        var expectedScopes = settings.Scopes.Length > 0
            ? settings.Scopes
            : platform == StreamingPlatform.Twitch
                ? (role == PlatformAuthRole.Bot ? [.. TwitchScopes.BotRequired] : [.. TwitchScopes.BroadcasterRequired])
                : [.. YouTubeScopes.RequiredFor(role)];
        var storedScopes = settings.StoredScopes;
        var scopeMismatch = storedScopes.Length > 0 && !new HashSet<string>(storedScopes, StringComparer.Ordinal).SetEquals(expectedScopes);

        var (statusText, severity) = DescribeTokenStatus(hasAccess, hasRefresh, scopeMismatch, settings.AccessTokenExpiresAt, now);
        var expiryText = settings.AccessTokenExpiresAt is { } expiresAt
            ? TimeZoneInfo.ConvertTime(expiresAt, TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm", UiCulture.Russian)
            : "неизвестно";

        return new PlatformAccountGrantViewModel(
            platform,
            accountId,
            accountDisplayName,
            role,
            contextId,
            isDefault,
            accessToken: settings.AccessToken,
            refreshToken: settings.RefreshToken,
            expectedScopes: expectedScopes,
            storedScopes: storedScopes,
            hasScopeMismatch: scopeMismatch,
            tokenStatusText: statusText,
            tokenStatusSeverity: severity,
            tokenExpiryText: expiryText,
            canAuthorize: canAuthorize,
            reauthorizeAsync: ReauthorizeGrantAsync,
            refreshAsync: RefreshGrantAsync,
            validateAsync: ValidateGrantAsync,
            saveManualEditsAsync: SaveManualGrantEditsAsync,
            removeRoleAsync: RemoveGrantRoleAsync);
    }

    private static (string Text, StatusSeverity Severity) DescribeTokenStatus(
        bool hasAccess,
        bool hasRefresh,
        bool scopeMismatch,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        if (!hasAccess && !hasRefresh)
        {
            return ("Токен отсутствует – нужен повторный вход", StatusSeverity.Warning);
        }

        if (scopeMismatch)
        {
            return ("Права изменились – требуется переавторизация", StatusSeverity.Warning);
        }

        if (hasAccess && expiresAt is { } expiry)
        {
            if (expiry > now)
            {
                var local = TimeZoneInfo.ConvertTime(expiry, TimeZoneInfo.Local).ToString("dd.MM HH:mm", UiCulture.Russian);
                return ($"Активен до {local}", StatusSeverity.Success);
            }

            return hasRefresh
                ? ("Истёк – обновится автоматически", StatusSeverity.Info)
                : ("Истёк – требуется повторный вход", StatusSeverity.Warning);
        }

        if (hasAccess)
        {
            return (hasRefresh ? "Авторизован (есть refresh-токен)" : "Авторизован", StatusSeverity.Success);
        }

        return ("Обновится автоматически по refresh-токену", StatusSeverity.Info);
    }

    private static string BuildAssignedChannelsSummary(string accountId, IReadOnlyList<ChannelCatalogEntry> channels)
    {
        List<string> usages = [];
        foreach (var channel in channels)
        {
            var isBot = string.Equals(channel.GetAccountAssignment(PlatformAuthRole.Bot)?.Account.AccountId, accountId, StringComparison.Ordinal);
            var isBroadcaster = string.Equals(channel.GetAccountAssignment(PlatformAuthRole.Broadcaster)?.Account.AccountId, accountId, StringComparison.Ordinal);
            var name = string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.ChannelId : channel.DisplayName;
            if (isBot && isBroadcaster)
            {
                usages.Add($"{name} (Бот, Стример)");
            }
            else if (isBot)
            {
                usages.Add($"{name} (Бот)");
            }
            else if (isBroadcaster)
            {
                usages.Add($"{name} (Стример)");
            }
        }

        return usages.Count == 0
            ? "Не назначен ни на один канал"
            : $"Назначен: {string.Join(", ", usages)}";
    }

    private void PublishAuthorizationRefreshed(StreamingPlatform platform, PlatformAuthRole role, string contextId)
    {
        if (_eventBus is not null)
        {
            _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(platform, role, AuthContextIds.Normalize(contextId)));
        }
    }

    private void SetNotice(string message, StatusSeverity severity)
    {
        StatusNoticeSeverity = severity;
        StatusNotice = message;
    }

    private static string[] ParseScopes(string text) =>
        text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public sealed partial class PlatformAccountCardViewModel : ObservableObject
{
    private readonly Func<PlatformAccountCardViewModel, PlatformAuthRole, Task> _addRole;
    private readonly Func<PlatformAccountCardViewModel, Task> _signOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopesToggleLabel))]
    private bool _isScopesExpanded;

    public PlatformAccountCardViewModel(
        StreamingPlatform platform,
        string accountId,
        string displayName,
        string assignedChannelsText,
        IReadOnlyList<PlatformAccountGrantViewModel> grants,
        bool canAuthorize,
        Func<PlatformAccountCardViewModel, PlatformAuthRole, Task> addRole,
        Func<PlatformAccountCardViewModel, Task> signOut)
    {
        Platform = platform;
        AccountId = accountId;
        DisplayName = displayName;
        AssignedChannelsText = assignedChannelsText;
        Grants = grants;
        CanAuthorize = canAuthorize;
        _addRole = addRole;
        _signOut = signOut;

        var botGrant = grants.FirstOrDefault(static g => g.Role == PlatformAuthRole.Bot);
        var broadcasterGrant = grants.FirstOrDefault(static g => g.Role == PlatformAuthRole.Broadcaster);
        HasBotRole = botGrant is not null;
        HasBroadcasterRole = broadcasterGrant is not null;
        BotBadgeSeverity = botGrant?.TokenStatusSeverity ?? StatusSeverity.None;
        BroadcasterBadgeSeverity = broadcasterGrant?.TokenStatusSeverity ?? StatusSeverity.None;
    }

    public StreamingPlatform Platform { get; }

    public string AccountId { get; }

    public string DisplayName { get; }

    public string AssignedChannelsText { get; }

    public IReadOnlyList<PlatformAccountGrantViewModel> Grants { get; }

    public bool CanAuthorize { get; }

    public bool HasBotRole { get; }

    public bool HasBroadcasterRole { get; }

    public bool CanAddBotRole => CanAuthorize && !HasBotRole;

    public bool CanAddBroadcasterRole => CanAuthorize && !HasBroadcasterRole;

    public StatusSeverity BotBadgeSeverity { get; }

    public StatusSeverity BroadcasterBadgeSeverity { get; }

    public string ScopesToggleLabel => IsScopesExpanded ? "Скрыть права доступа" : "Права доступа";

    [RelayCommand]
    private void ToggleScopes() => IsScopesExpanded = !IsScopesExpanded;

    [RelayCommand(CanExecute = nameof(CanAddBotRole))]
    private Task AddBotRoleAsync() => _addRole(this, PlatformAuthRole.Bot);

    [RelayCommand(CanExecute = nameof(CanAddBroadcasterRole))]
    private Task AddBroadcasterRoleAsync() => _addRole(this, PlatformAuthRole.Broadcaster);

    [RelayCommand]
    private Task SignOutAsync() => _signOut(this);
}

public sealed partial class PlatformAccountGrantViewModel : ObservableObject
{
    private readonly Func<PlatformAccountGrantViewModel, Task> _reauthorizeAsync;
    private readonly Func<PlatformAccountGrantViewModel, Task> _refreshAsync;
    private readonly Func<PlatformAccountGrantViewModel, Task> _validateAsync;
    private readonly Func<PlatformAccountGrantViewModel, Task> _saveManualEditsAsync;
    private readonly Func<PlatformAccountGrantViewModel, Task> _removeRoleAsync;
    private readonly string _initialAccessToken;
    private readonly string _initialRefreshToken;
    private readonly string _initialScopesText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualEdits))]
    [NotifyCanExecuteChangedFor(nameof(SaveManualEditsCommand), nameof(ValidateTokenCommand))]
    private string _accessTokenValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualEdits))]
    [NotifyCanExecuteChangedFor(nameof(SaveManualEditsCommand), nameof(RefreshTokenCommand))]
    private string _refreshTokenValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualEdits))]
    [NotifyCanExecuteChangedFor(nameof(SaveManualEditsCommand))]
    private string _scopesText;

    [ObservableProperty]
    private string? _actionStatusText;

    [ObservableProperty]
    private StatusSeverity _actionStatusSeverity;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReauthorizeCommand), nameof(RefreshTokenCommand), nameof(ValidateTokenCommand),
        nameof(SaveManualEditsCommand), nameof(RemoveRoleCommand), nameof(ResetScopesCommand))]
    private bool _isWorking;

    public PlatformAccountGrantViewModel(
        StreamingPlatform platform,
        string accountId,
        string accountDisplayName,
        PlatformAuthRole role,
        string contextId,
        bool isDefault,
        string accessToken,
        string refreshToken,
        string[] expectedScopes,
        string[] storedScopes,
        bool hasScopeMismatch,
        string tokenStatusText,
        StatusSeverity tokenStatusSeverity,
        string tokenExpiryText,
        bool canAuthorize,
        Func<PlatformAccountGrantViewModel, Task> reauthorizeAsync,
        Func<PlatformAccountGrantViewModel, Task> refreshAsync,
        Func<PlatformAccountGrantViewModel, Task> validateAsync,
        Func<PlatformAccountGrantViewModel, Task> saveManualEditsAsync,
        Func<PlatformAccountGrantViewModel, Task> removeRoleAsync)
    {
        Platform = platform;
        AccountId = accountId;
        AccountDisplayName = accountDisplayName;
        Role = role;
        ContextId = contextId;
        IsDefault = isDefault;
        CanAuthorize = canAuthorize;
        TokenStatusText = tokenStatusText;
        TokenStatusSeverity = tokenStatusSeverity;
        TokenExpiryText = tokenExpiryText;
        HasScopeMismatch = hasScopeMismatch;
        ExpectedScopesSummary = expectedScopes.Length > 0 ? string.Join(" ", expectedScopes) : "–";
        GrantedScopesSummary = storedScopes.Length > 0 ? string.Join(" ", storedScopes) : "–";

        _initialAccessToken = accessToken ?? string.Empty;
        _initialRefreshToken = refreshToken ?? string.Empty;
        _initialScopesText = string.Join(" ", expectedScopes);
        _accessTokenValue = _initialAccessToken;
        _refreshTokenValue = _initialRefreshToken;
        _scopesText = _initialScopesText;

        _reauthorizeAsync = reauthorizeAsync;
        _refreshAsync = refreshAsync;
        _validateAsync = validateAsync;
        _saveManualEditsAsync = saveManualEditsAsync;
        _removeRoleAsync = removeRoleAsync;
    }

    public StreamingPlatform Platform { get; }

    public string AccountId { get; }

    public string AccountDisplayName { get; }

    public PlatformAuthRole Role { get; }

    public string RoleTitle => Role == PlatformAuthRole.Bot ? "Бот" : "Стример";

    public string ContextId { get; }

    public bool IsDefault { get; }

    public string ContextDisplay => IsDefault ? "Основной (default)" : ContextId;

    public bool CanAuthorize { get; }

    public bool SupportsEditableScopes => Platform == StreamingPlatform.Twitch;

    public bool SupportsTokenValidation => Platform == StreamingPlatform.Twitch;

    public string TokenStatusText { get; }

    public StatusSeverity TokenStatusSeverity { get; }

    public string TokenExpiryText { get; }

    public string ExpectedScopesSummary { get; }

    public string GrantedScopesSummary { get; }

    public bool HasScopeMismatch { get; }

    public bool HasManualEdits =>
        !string.Equals(AccessTokenValue, _initialAccessToken, StringComparison.Ordinal)
        || !string.Equals(RefreshTokenValue, _initialRefreshToken, StringComparison.Ordinal)
        || (SupportsEditableScopes && !string.Equals(ScopesText, _initialScopesText, StringComparison.Ordinal));

    public string[] GetRequestedScopes()
    {
        if (Platform != StreamingPlatform.Twitch)
        {
            return [.. YouTubeScopes.RequiredFor(Role)];
        }

        var required = Role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired;
        var parsed = ScopesText.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. required.Union(parsed, StringComparer.Ordinal)];
    }

    public void SetActionStatus(string message, StatusSeverity severity)
    {
        ActionStatusSeverity = severity;
        ActionStatusText = message;
    }

    private bool CanReauthorize() => CanAuthorize && !IsWorking;

    private bool CanRefresh() => !IsWorking && !string.IsNullOrWhiteSpace(RefreshTokenValue);

    private bool CanValidate() => SupportsTokenValidation && !IsWorking && !string.IsNullOrWhiteSpace(AccessTokenValue);

    private bool CanSaveManualEdits() => !IsWorking && HasManualEdits;

    private bool CanResetScopes() => SupportsEditableScopes && !IsWorking;

    private bool CanRemoveRole() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanReauthorize))]
    private async Task ReauthorizeAsync()
    {
        IsWorking = true;
        try
        {
            await _reauthorizeAsync(this);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshTokenAsync()
    {
        IsWorking = true;
        try
        {
            await _refreshAsync(this);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateTokenAsync()
    {
        IsWorking = true;
        try
        {
            await _validateAsync(this);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetScopes))]
    private void ResetScopes()
    {
        ScopesText = Role == PlatformAuthRole.Bot
            ? string.Join(" ", [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional])
            : string.Join(" ", TwitchScopes.BroadcasterRequired);
        SetActionStatus("Права сброшены к стандартным. Нажмите «Сохранить правки» или «Переавторизовать».", StatusSeverity.Info);
    }

    [RelayCommand(CanExecute = nameof(CanSaveManualEdits))]
    private async Task SaveManualEditsAsync()
    {
        IsWorking = true;
        try
        {
            await _saveManualEditsAsync(this);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveRole))]
    private async Task RemoveRoleAsync()
    {
        IsWorking = true;
        try
        {
            await _removeRoleAsync(this);
        }
        finally
        {
            IsWorking = false;
        }
    }
}
