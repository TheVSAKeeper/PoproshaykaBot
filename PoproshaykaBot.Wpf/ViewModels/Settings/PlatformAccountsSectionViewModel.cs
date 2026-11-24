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
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class PlatformAccountsSectionViewModel : ObservableObject
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly IDialogService? _dialogService;
    private readonly IEventBus? _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlatformAccountsSectionViewModel>? _logger;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformAccountDirectory> _accountDirectories;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformAccountLogin> _accountLogins;
    private readonly IPlatformAuthRegistry? _authRegistry;
    private readonly Dictionary<StreamingPlatform, Func<string?>> _unsavedCredentialChecks = [];
    private readonly Dictionary<GrantDraftKey, GrantDraft> _unsavedDrafts = [];

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
        IChatPlatformRegistry? platformRegistry = null,
        IEnumerable<IPlatformAccountDirectory>? accountDirectories = null,
        IEnumerable<IPlatformAccountLogin>? accountLogins = null,
        IPlatformAuthRegistry? authRegistry = null,
        IPlatformWebChatLoginDialog? webChatLoginDialog = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        _catalogStore = catalogStore;
        _settingsManager = settingsManager;
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _dialogService = dialogService;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _platformRegistry = platformRegistry;
        _accountDirectories = PlatformAccountLoginDefaults.BuildDirectories(
            accountDirectories, twitchAccountsStore, youtubeAccountsStore);
        _accountLogins = PlatformAccountLoginDefaults.Build(
            accountLogins,
            settingsManager,
            twitchAccountsStore,
            twitchOAuth,
            youtubeSettingsStore,
            youtubeAccountsStore,
            youtubeOAuth,
            shellLauncher,
            _timeProvider,
            embeddedTwitchAuth,
            webChatLoginDialog,
            eventBus);
        _authRegistry = authRegistry;
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

    public bool SupportsBrowserToggle => _accountLogins.GetValueOrDefault(SelectedPlatform)?.SupportsEmbeddedBrowser == true;

    public bool CanAuthorize => CanAuthorizePlatform(SelectedPlatform);

    internal void SetCredentialDrafts(OAuthSettingsViewModel? twitch, YouTubeSettingsSectionViewModel? youtube)
    {
        _unsavedCredentialChecks.Clear();
        if (twitch is not null && _settingsManager is not null)
        {
            _unsavedCredentialChecks[StreamingPlatform.Twitch] = () =>
            {
                var settings = _settingsManager.Current.Twitch;
                return twitch.ClientId.Trim() != settings.ClientId
                    || twitch.ClientSecret.Trim() != settings.ClientSecret
                    || twitch.RedirectUri.Trim() != settings.RedirectUri
                    ? "Сначала сохраните новые реквизиты во вкладке «Параметры и доступ», затем повторите вход."
                    : null;
            };
        }

        if (youtube is not null && _youtubeSettingsStore is not null && _youtubeAccountsStore is not null)
        {
            _unsavedCredentialChecks[StreamingPlatform.YouTube] = () =>
            {
                var settings = _youtubeSettingsStore.Load();
                var secret = _youtubeAccountsStore.LoadClientSecret();
                return youtube.ClientId.Trim() != settings.ClientId || youtube.ClientSecret.Trim() != secret
                    ? "Сначала сохраните новые реквизиты во вкладке «Параметры и доступ», затем повторите вход."
                    : null;
            };
        }
    }

    public bool SupportsAccountsFor(StreamingPlatform? platform) =>
        platform is { } id && _accountDirectories.ContainsKey(id);

    private bool CanAuthorizePlatform(StreamingPlatform platform) =>
        _accountLogins.GetValueOrDefault(platform)?.CanAuthorize == true;

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        StatusNotice = null;
        Load();
    }

    public void Load() => LoadInternal(dropDraft: null);

    internal void DiscardUnsavedManualEdits()
    {
        _unsavedDrafts.Clear();
        LoadInternal(dropDraft: null, captureCurrentDrafts: false);
    }

    private void LoadInternal(GrantDraftKey? dropDraft, bool captureCurrentDrafts = true)
    {
        if (captureCurrentDrafts)
        {
            CaptureUnsavedDrafts();
        }

        if (dropDraft is { } keyToDrop)
        {
            _unsavedDrafts.Remove(keyToDrop);
        }

        var expandedPermissions = Accounts.SelectMany(static card => card.Grants)
            .Where(static grant => grant.IsPermissionsExpanded)
            .Select(static grant => new GrantDraftKey(grant.Platform, grant.Role, grant.ContextId))
            .ToHashSet();

        Accounts.Clear();
        if (!SupportsAccounts)
        {
            NotifyCountChanged();
            return;
        }

        var platform = SelectedPlatform;
        var directory = _accountDirectories[platform];
        var login = _accountLogins.GetValueOrDefault(platform);
        var platformAccounts = directory.ListAccounts();
        var channels = _catalogStore.Load().Channels
            .Where(c => c.Platform == platform && c.IsConfigured)
            .ToArray();
        var now = _timeProvider.GetUtcNow();
        var canAuth = CanAuthorizePlatform(platform);
        var supportsWebChat = directory.SupportsChatBrowserProfile && login?.SupportsWebChatSignIn == true;
        var oauthMethodTitle = login?.OAuthMethod.MethodTitle;

        foreach (var account in platformAccounts)
        {
            var grants = BuildGrantViewModels(platform, account, channels, now, canAuth, login);
            foreach (var grant in grants)
            {
                grant.IsPermissionsExpanded = expandedPermissions.Contains(new(grant.Platform, grant.Role, grant.ContextId));
            }

            var assignedSummary = BuildAssignedChannelsSummary(account.Key.AccountId, channels);
            var hasWebChatSession = account.Authorizations.Any(static auth => auth.ChatBrowserProfile is not null);
            var (apiMethod, webChatMethod) = BuildAuthMethodDescriptors(grants, oauthMethodTitle, supportsWebChat, hasWebChatSession);

            var card = new PlatformAccountCardViewModel(
                platform,
                account.Key.AccountId,
                account.DisplayName,
                assignedSummary,
                grants,
                canAuth,
                supportsWebChat,
                hasWebChatSession,
                apiMethod,
                webChatMethod,
                oauthMethodTitle,
                AddRoleForAccountAsync,
                SignOutAccountAsync,
                SignInWebChatAsync,
                ResetWebChatAsync)
            {
                CanRemove = directory.CanRemove,
            };
            Accounts.Add(card);
        }

        NotifyCountChanged();
    }

    private static (PlatformAuthMethodDescriptor ApiMethod, PlatformAuthMethodDescriptor? WebChatMethod) BuildAuthMethodDescriptors(
        IReadOnlyList<PlatformAccountGrantViewModel> grants,
        string? oauthMethodTitle,
        bool supportsWebChat,
        bool hasWebChatSession)
    {
        var anyReady = grants.Any(static g => g.TokenStatusSeverity == StatusSeverity.Success);
        var anyAttention = grants.Any(static g => g.TokenStatusSeverity == StatusSeverity.Warning);
        var anyInfo = grants.Any(static g => g.TokenStatusSeverity == StatusSeverity.Info);
        var rolesSummary = grants.Count > 0
            ? string.Join(", ", grants.Select(static g => g.RoleTitle))
            : "нет ролей";

        var (apiStatus, apiText) = anyAttention
            ? (PlatformAuthMethodStatus.Attention, $"{rolesSummary} · требуется внимание")
            : anyReady
                ? (PlatformAuthMethodStatus.Ready, $"{rolesSummary} · токены готовы")
                : anyInfo
                    ? (PlatformAuthMethodStatus.Ready, $"{rolesSummary} · обновится по refresh-токену")
                    : (PlatformAuthMethodStatus.Missing, "Токены отсутствуют");

        var apiMethod = new PlatformAuthMethodDescriptor("api-oauth", "API / OAuth", apiStatus, apiText, oauthMethodTitle);
        PlatformAuthMethodDescriptor? webChatMethod = supportsWebChat
            ? new(
                "web-chat",
                "Веб-чат",
                hasWebChatSession ? PlatformAuthMethodStatus.Ready : PlatformAuthMethodStatus.Missing,
                hasWebChatSession ? "Подключён" : "Не подключён")
            : null;

        return (apiMethod, webChatMethod);
    }

    internal void RefreshIfPlatform(StreamingPlatform platform) => RefreshIfPlatform(platform, dropDraft: null);

    private void RefreshIfPlatform(StreamingPlatform platform, GrantDraftKey? dropDraft)
    {
        if (dropDraft is { } keyToDrop)
        {
            _unsavedDrafts.Remove(keyToDrop);
        }

        if (SelectedPlatform == platform)
        {
            LoadInternal(dropDraft);
        }
    }

    private void CaptureUnsavedDrafts()
    {
        foreach (var card in Accounts)
        {
            foreach (var grant in card.Grants)
            {
                var key = new GrantDraftKey(grant.Platform, grant.Role, grant.ContextId);
                if (grant.HasManualEdits)
                {
                    _unsavedDrafts[key] = new(
                        grant.IsAccessTokenEdited ? grant.AccessTokenValue : null,
                        grant.IsRefreshTokenEdited ? grant.RefreshTokenValue : null,
                        grant.IsScopesEdited ? grant.ScopesText : null);
                }
                else
                {
                    _unsavedDrafts.Remove(key);
                }
            }
        }
    }

    private void DropDraftsFor(StreamingPlatform platform, PlatformAuthRole? role, IEnumerable<string> contextIds)
    {
        foreach (var contextId in contextIds)
        {
            var normalized = AuthContextIds.Normalize(contextId);
            if (role is { } targetRole)
            {
                _unsavedDrafts.Remove(new(platform, targetRole, normalized));
            }
            else
            {
                _unsavedDrafts.Remove(new(platform, PlatformAuthRole.Bot, normalized));
                _unsavedDrafts.Remove(new(platform, PlatformAuthRole.Broadcaster, normalized));
            }
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

        if (_unsavedCredentialChecks.TryGetValue(platform, out var check) && check() is { } unsavedWarning)
        {
            SetNotice(unsavedWarning, StatusSeverity.Warning);
            return;
        }

        if (!_accountLogins.TryGetValue(platform, out var login))
        {
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
            var succeeded = await AuthorizeWithAdapterAsync(login, combined, role, targetAccountId, preferredContextId, customScopes);
            if (succeeded)
            {
                RefreshIfPlatform(platform);
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
            SetNotice($"Не удалось подключить аккаунт. {StreamingErrorMessages.SafeMessage(exception, platform, _platformRegistry)}", StatusSeverity.Error);
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    private async Task<bool> AuthorizeWithAdapterAsync(
        IPlatformAccountLogin login,
        bool combined,
        PlatformAuthRole role,
        string? targetAccountId,
        string? preferredContextId,
        string[]? customScopes)
    {
        var result = await login.AuthorizeAsync(new(
            combined ? PlatformAuthRoles.Active : [role],
            targetAccountId,
            preferredContextId,
            customScopes ?? [],
            UseSystemBrowser));

        foreach (var grant in result.ChangedAuthorizations)
        {
            DropDraftsFor(login.Platform, grant.Role, [grant.ContextId]);
            PublishAuthorizationRefreshed(login.Platform, grant.Role, grant.ContextId);
        }

        if (result.ChangedAuthorizations.Count > 0 && !result.ChangedAuthorizations.All(grant =>
            grant.SubjectId is not null && IsAuthorizationPersisted(login.Platform, grant.Role, grant.ContextId, grant.SubjectId)))
        {
            SetNotice("Не удалось подтвердить запись авторизации на диске.", StatusSeverity.Warning);
            return true;
        }

        SetNotice(result.Message, result.Severity);
        return result.ChangedAuthorizations.Count > 0;
    }

    private async Task SignInWebChatAsync(PlatformAccountCardViewModel card)
    {
        if (!_accountLogins.TryGetValue(card.Platform, out var login) || !login.SupportsWebChatSignIn)
        {
            return;
        }

        var result = await login.SignInWebChatAsync(card.AccountId, card.DisplayName);
        if (result.Outcome != PlatformWebChatLoginOutcome.Completed)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                SetNotice(result.Message, result.Severity);
            }

            return;
        }

        PublishAccountRefreshed(card);
        RefreshIfPlatform(card.Platform);
        AccountsChanged?.Invoke(this, card.Platform);
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            SetNotice(result.Message, result.Severity);
        }
    }

    private Task ResetWebChatAsync(PlatformAccountCardViewModel card)
    {
        if (!_accountDirectories.TryGetValue(card.Platform, out var directory) || !directory.SupportsChatBrowserProfile)
        {
            return Task.CompletedTask;
        }

        if (_dialogService is not null
            && !_dialogService.Confirm("Сброс веб-сессии",
                $"Отвязать браузерную сессию веб-чата для аккаунта «{card.DisplayName}»?"))
        {
            return Task.CompletedTask;
        }

        var existingProfiles = directory.ListAccounts()
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, card.AccountId, StringComparison.Ordinal))
            ?.Authorizations
            .Select(static g => g.ChatBrowserProfile)
            .Where(static p => p is not null)
            .Distinct()
            .ToArray() ?? [];

        bool written;
        try
        {
            written = directory.SetChatBrowserProfile(card.AccountId, null);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Ошибка сброса веб-профиля чата для {Platform} {AccountId}", card.Platform, card.AccountId);
            SetNotice("Не удалось сбросить веб-сессию: ошибка записи файла аккаунтов.", StatusSeverity.Error);
            return Task.CompletedTask;
        }

        foreach (var profile in existingProfiles)
        {
            TryDeleteWebViewProfileDirectory(profile!);
        }

        PublishAccountRefreshed(card);
        RefreshIfPlatform(card.Platform);
        AccountsChanged?.Invoke(this, card.Platform);
        SetNotice(written
            ? $"Веб-сессия для «{card.DisplayName}» сброшена."
            : "Веб-сессия сброшена в памяти до перезапуска – записать в файл не удалось.",
            written ? StatusSeverity.Info : StatusSeverity.Warning);
        return Task.CompletedTask;
    }

    private static void TryDeleteWebViewProfileDirectory(PlatformChatBrowserProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.UserDataFolder) || string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            return;
        }

        try
        {
            var profilePath = System.IO.Path.Combine(profile.UserDataFolder, "EBWebView", profile.ProfileName);
            if (System.IO.Directory.Exists(profilePath))
            {
                System.IO.Directory.Delete(profilePath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }

    private void PublishAccountRefreshed(PlatformAccountCardViewModel card)
    {
        if (card.Grants.Count == 0)
        {
            PublishAuthorizationRefreshed(card.Platform, PlatformAuthRole.Bot, AuthContextIds.Default);
            return;
        }

        foreach (var grant in card.Grants)
        {
            PublishAuthorizationRefreshed(card.Platform, grant.Role, grant.ContextId);
        }
    }

    private Task SignOutAccountAsync(PlatformAccountCardViewModel card)
    {
        if (!_accountDirectories.TryGetValue(card.Platform, out var directory) || !directory.CanRemove)
        {
            return Task.CompletedTask;
        }

        var prompt = string.IsNullOrWhiteSpace(card.AssignedChannelsText) || card.AssignedChannelsText.StartsWith("Не назначен", StringComparison.Ordinal)
            ? $"Удалить токены и выйти из аккаунта «{card.DisplayName}»?"
            : $"Удалить токены и выйти из аккаунта «{card.DisplayName}»?\n\n{card.AssignedChannelsText}. После выхода на этих каналах нужно будет выбрать другой аккаунт или снять назначение.";

        if (_dialogService is not null && !_dialogService.Confirm("Выход из аккаунта", prompt))
        {
            return Task.CompletedTask;
        }

        var (removed, written) = directory.RemoveAccount(card.AccountId);
        foreach (var (role, contextId) in removed)
        {
            ResetAuthorization(card.Platform, role, contextId);
        }

        _logger?.PlatformAccountSignedOut(card.Platform, card.AccountId);
        DropDraftsFor(card.Platform, role: null, removed.Select(static r => r.ContextId));
        RefreshIfPlatform(card.Platform);
        AccountsChanged?.Invoke(this, card.Platform);
        SetNotice(written
            ? $"Аккаунт «{card.DisplayName}» удалён."
            : $"Аккаунт «{card.DisplayName}» очищён в памяти до перезапуска.",
            written ? StatusSeverity.Info : StatusSeverity.Warning);
        return Task.CompletedTask;
    }

    private Task RemoveGrantRoleAsync(PlatformAccountGrantViewModel grant)
    {
        if (!_accountDirectories.TryGetValue(grant.Platform, out var directory) || !directory.CanRemove)
        {
            return Task.CompletedTask;
        }

        if (_dialogService is not null
            && !_dialogService.Confirm("Удаление роли аккаунта",
                $"Удалить права роли «{grant.RoleTitle}» у аккаунта «{grant.AccountDisplayName}»?"))
        {
            return Task.CompletedTask;
        }

        var (removedContexts, written) = directory.RemoveAccountRole(grant.AccountId, grant.Role);
        foreach (var contextId in removedContexts)
        {
            ResetAuthorization(grant.Platform, grant.Role, contextId);
        }

        if (removedContexts.Count > 0)
        {
            _logger?.PlatformAccountRoleRemoved(grant.Platform, grant.Role, grant.AccountId);
            DropDraftsFor(grant.Platform, grant.Role, removedContexts);
            RefreshIfPlatform(grant.Platform);
            AccountsChanged?.Invoke(this, grant.Platform);
            SetNotice(written
                ? $"Роль «{grant.RoleTitle}» удалена у аккаунта «{grant.AccountDisplayName}»."
                : "Изменение принято в памяти до перезапуска.",
                written ? StatusSeverity.Info : StatusSeverity.Warning);
        }

        return Task.CompletedTask;
    }

    private void ResetAuthorization(StreamingPlatform platform, PlatformAuthRole role, string contextId)
    {
        if (_authRegistry?.TryGet(platform, out var auth) == true)
        {
            auth.ResetRetryState(role, contextId);
        }
        else if (_accountLogins.TryGetValue(platform, out var login))
        {
            login.ResetRetryState(role, contextId);
        }

        PublishAuthorizationRefreshed(platform, role, contextId);
    }

    private async Task ValidateGrantAsync(PlatformAccountGrantViewModel grant)
    {
        if (!_accountLogins.TryGetValue(grant.Platform, out var login) || !login.SupportsTokenValidation)
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
        var result = await login.ValidateTokenAsync(grant.Role, token, CancellationToken.None);
        grant.SetActionStatus(result.Message, result.Severity);
    }

    private async Task RefreshGrantAsync(PlatformAccountGrantViewModel grant)
    {
        if (!_accountLogins.TryGetValue(grant.Platform, out var login))
        {
            return;
        }

        if (grant.IsRefreshTokenEdited)
        {
            grant.SetActionStatus("Сначала сохраните изменённый Refresh Token, затем нажмите «Обновить».", StatusSeverity.Warning);
            return;
        }

        var refreshToken = grant.RefreshTokenValue.Trim();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            grant.SetActionStatus("Refresh Token отсутствует – пройдите вход заново.", StatusSeverity.Warning);
            return;
        }

        grant.SetActionStatus("Обновление токена…", StatusSeverity.Info);
        var result = await login.RefreshTokenAsync(grant.Role, grant.ContextId, refreshToken, CancellationToken.None);
        if (!result.Succeeded)
        {
            grant.SetActionStatus(result.Message, result.Severity);
            return;
        }

        var refreshedAt = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZoneInfo.Local)
            .ToString("dd.MM.yyyy HH:mm:ss", UiCulture.Russian);
        RefreshIfPlatform(grant.Platform, dropDraft: new(grant.Platform, grant.Role, grant.ContextId));
        AccountsChanged?.Invoke(this, grant.Platform);
        if (result.Persisted && IsAuthorizationPersisted(grant.Platform, grant.Role, grant.ContextId, grant.AccountId))
        {
            SetNotice($"Токен ({grant.RoleTitle}, «{grant.AccountDisplayName}») обновлён ({refreshedAt}). Запись проверена.", StatusSeverity.Success);
        }
        else if (result.Persisted)
        {
            SetNotice("Токен обновлён, но подтвердить запись на диске не удалось.", StatusSeverity.Warning);
        }
        else
        {
            SetNotice($"Токен ({grant.RoleTitle}, «{grant.AccountDisplayName}») обновлён только в памяти до перезапуска – записать в файл не удалось.", StatusSeverity.Warning);
        }
    }

    private Task SaveManualGrantEditsAsync(PlatformAccountGrantViewModel grant)
    {
        if (!_accountLogins.TryGetValue(grant.Platform, out var login))
        {
            return Task.CompletedTask;
        }

        var result = login.SaveManualEdits(grant.Role, grant.ContextId, new(
            grant.IsAccessTokenEdited,
            grant.AccessTokenValue,
            grant.InitialAccessToken,
            grant.IsRefreshTokenEdited,
            grant.RefreshTokenValue,
            grant.InitialRefreshToken,
            grant.IsScopesEdited,
            grant.ScopesText));

        RefreshIfPlatform(grant.Platform, dropDraft: new(grant.Platform, grant.Role, grant.ContextId));
        if (result.Conflict || !result.Applied)
        {
            SetNotice(result.Message, result.Severity);
            return Task.CompletedTask;
        }

        AccountsChanged?.Invoke(this, grant.Platform);
        if (result.Written && IsAuthorizationPersisted(grant.Platform, grant.Role, grant.ContextId, grant.AccountId))
        {
            SetNotice($"Правки токена ({grant.RoleTitle}, «{grant.AccountDisplayName}») сохранены. Запись проверена.", StatusSeverity.Success);
        }
        else
        {
            SetNotice(result.Written ? "Не удалось подтвердить запись правок на диске." : "Правки приняты в памяти до перезапуска.",
                StatusSeverity.Warning);
        }

        return Task.CompletedTask;
    }

    private bool IsAuthorizationPersisted(StreamingPlatform platform, PlatformAuthRole role, string contextId, string accountId) =>
        _accountDirectories.GetValueOrDefault(platform)?.VerifyPersistedAuthorization(role, contextId, accountId) == true;

    private IReadOnlyList<PlatformAccountGrantViewModel> BuildGrantViewModels(
        StreamingPlatform platform,
        PlatformAccount account,
        IReadOnlyList<ChannelCatalogEntry> channels,
        DateTimeOffset now,
        bool canAuthorize,
        IPlatformAccountLogin? login)
    {
        var directory = _accountDirectories[platform];
        var grants = new List<PlatformAccountGrantViewModel>();
        foreach (var role in PlatformAuthRoles.Active)
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

            if (directory.LoadAuthorization(role, summary.ContextId) is { } settings)
            {
                grants.Add(BuildGrantViewModel(platform, account.Key.AccountId, account.DisplayName, role, summary.ContextId,
                    summary.IsDefault, settings, now, canAuthorize, directory.GetPermissions(role), channels, login));
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
        bool canAuthorize,
        IReadOnlyList<PlatformAccountOperation> permissions,
        IReadOnlyList<ChannelCatalogEntry> channels,
        IPlatformAccountLogin? login)
    {
        var hasAccess = !string.IsNullOrWhiteSpace(settings.AccessToken);
        var hasRefresh = !string.IsNullOrWhiteSpace(settings.RefreshToken);
        var storedScopes = settings.StoredScopes;
        var expectedScopes = settings.Scopes;
        var scopeMismatch = login is not null
            ? login.TryEvaluateScopeMismatch(role, expectedScopes, storedScopes, out expectedScopes)
            : storedScopes.Length > 0 && !new HashSet<string>(storedScopes, StringComparer.Ordinal).SetEquals(expectedScopes);

        var (statusText, severity) = DescribeTokenStatus(hasAccess, hasRefresh, scopeMismatch, settings.AccessTokenExpiresAt, now);
        var expiryText = settings.AccessTokenExpiresAt is { } expiresAt
            ? TimeZoneInfo.ConvertTime(expiresAt, TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm", UiCulture.Russian)
            : "неизвестно";

        var vm = new PlatformAccountGrantViewModel(
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
            login: login,
            reauthorizeAsync: ReauthorizeGrantAsync,
            refreshAsync: RefreshGrantAsync,
            validateAsync: ValidateGrantAsync,
            saveManualEditsAsync: SaveManualGrantEditsAsync,
            removeRoleAsync: RemoveGrantRoleAsync)
        {
            CanRemove = _accountDirectories[platform].CanRemove,
            Permissions = DescribePermissions(login, permissions, channels, accountId, role, contextId, storedScopes,
                hasAccess, hasRefresh, scopeMismatch, settings.AccessTokenExpiresAt, now),
        };

        if (_unsavedDrafts.TryGetValue(new(platform, role, AuthContextIds.Normalize(contextId)), out var draft))
        {
            vm.ApplyDraft(draft.AccessToken, draft.RefreshToken, draft.ScopesText);
        }

        return vm;
    }

    private static IReadOnlyList<AccountPermissionStatus> DescribePermissions(
        IPlatformAccountLogin? login,
        IReadOnlyList<PlatformAccountOperation> permissions,
        IReadOnlyList<ChannelCatalogEntry> channels,
        string accountId,
        PlatformAuthRole role,
        string contextId,
        string[] scopes,
        bool hasAccess,
        bool hasRefresh,
        bool scopeMismatch,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        var assignedChannels = channels
            .Select(channel => (Channel: channel, Assignment: channel.GetAccountAssignment(role)))
            .Where(value => value.Assignment is { } assignment
                && string.Equals(assignment.Account.AccountId, accountId, StringComparison.Ordinal)
                && string.Equals(assignment.ContextId, contextId, StringComparison.Ordinal))
            .ToArray();
        var granted = scopes.ToHashSet(StringComparer.Ordinal);
        var treatMismatchAsSessionFailure = scopeMismatch
            && (login?.ShouldTreatScopeMismatchAsSessionFailure(role, granted) ?? true);
        var sessionStatus = treatMismatchAsSessionFailure || (!hasRefresh && (!hasAccess || expiresAt <= now))
            ? ("Нужен вход", StatusSeverity.Warning)
            : !hasAccess || expiresAt <= now
                ? ("Токен нужно обновить", StatusSeverity.Info)
                : ((string?)null, StatusSeverity.None);
        var result = new List<AccountPermissionStatus>();
        foreach (var permission in permissions)
        {
            var (message, severity) = sessionStatus;
            if (message is null && permission.RequiredScopes.Length > 0)
            {
                if (granted.Count == 0)
                {
                    (message, severity) = ("Выданные права не подтверждены", StatusSeverity.Info);
                }
                else if (!permission.RequiredScopes.All(required => login?.IsScopeSatisfied(granted, required) ?? granted.Contains(required)))
                {
                    (message, severity) = ("Не хватает прав", StatusSeverity.Warning);
                }
            }

            if (message is not null || !permission.IsChannelScoped)
            {
                result.Add(new(permission.Name, message ?? "Право выдано", message is null ? StatusSeverity.Success : severity));
            }
            else if (assignedChannels.Length == 0)
            {
                result.Add(new(permission.Name, "Назначьте аккаунт на канал", StatusSeverity.Info));
            }
            else
            {
                foreach (var (channel, assignment) in assignedChannels)
                {
                    var channelName = string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.ChannelId : channel.DisplayName;
                    result.Add(new($"{permission.Name} · {channelName}",
                        assignment!.IsVerified ? "Право выдано · канал проверен" : "Доступ к каналу не проверен",
                        assignment.IsVerified ? StatusSeverity.Success : StatusSeverity.Info));
                }
            }
        }

        return result;
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

    private readonly record struct GrantDraftKey(StreamingPlatform Platform, PlatformAuthRole Role, string ContextId);

    private readonly record struct GrantDraft(string? AccessToken, string? RefreshToken, string? ScopesText);
}

public sealed partial class PlatformAccountCardViewModel : ObservableObject
{
    private readonly Func<PlatformAccountCardViewModel, PlatformAuthRole, Task> _addRole;
    private readonly Func<PlatformAccountCardViewModel, Task> _signOut;
    private readonly Func<PlatformAccountCardViewModel, Task> _signInWebChat;
    private readonly Func<PlatformAccountCardViewModel, Task> _resetWebChat;

    public PlatformAccountCardViewModel(
        StreamingPlatform platform,
        string accountId,
        string displayName,
        string assignedChannelsText,
        IReadOnlyList<PlatformAccountGrantViewModel> grants,
        bool canAuthorize,
        bool supportsWebChat,
        bool hasWebChatSession,
        PlatformAuthMethodDescriptor apiAuthMethod,
        PlatformAuthMethodDescriptor? webChatMethod,
        string? oauthMethodLabel,
        Func<PlatformAccountCardViewModel, PlatformAuthRole, Task> addRole,
        Func<PlatformAccountCardViewModel, Task> signOut,
        Func<PlatformAccountCardViewModel, Task> signInWebChat,
        Func<PlatformAccountCardViewModel, Task> resetWebChat)
    {
        Platform = platform;
        AccountId = accountId;
        DisplayName = displayName;
        AssignedChannelsText = assignedChannelsText;
        Grants = grants;
        CanAuthorize = canAuthorize;
        SupportsWebChat = supportsWebChat;
        HasWebChatSession = hasWebChatSession;
        ApiAuthMethod = apiAuthMethod;
        WebChatMethod = webChatMethod;
        OAuthMethodLabel = oauthMethodLabel;
        AuthMethods = webChatMethod is null ? [apiAuthMethod] : [apiAuthMethod, webChatMethod];
        _addRole = addRole;
        _signOut = signOut;
        _signInWebChat = signInWebChat;
        _resetWebChat = resetWebChat;

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

    public bool CanRemove { get; init; }

    public bool SupportsWebChat { get; }

    public bool HasWebChatSession { get; }

    public PlatformAuthMethodDescriptor ApiAuthMethod { get; }

    public PlatformAuthMethodDescriptor? WebChatMethod { get; }

    public IReadOnlyList<PlatformAuthMethodDescriptor> AuthMethods { get; }

    public string? OAuthMethodLabel { get; }

    public bool HasOAuthMethodLabel => !string.IsNullOrWhiteSpace(OAuthMethodLabel);

    public string ApiAuthStatusText => $"API / OAuth: {ApiAuthMethod.StatusText}";

    public StatusSeverity ApiAuthStatusSeverity => MapStatusSeverity(ApiAuthMethod.Status);

    public string WebChatStatusText => HasWebChatSession ? "Веб-чат: подключён" : "Веб-чат: не подключён";

    public StatusSeverity WebChatStatusSeverity => HasWebChatSession ? StatusSeverity.Success : StatusSeverity.Info;

    public bool CanSignInWebChat => SupportsWebChat && !HasWebChatSession;

    public bool CanResetWebChat => SupportsWebChat && HasWebChatSession;

    public bool HasBotRole { get; }

    public bool HasBroadcasterRole { get; }

    public bool CanAddBotRole => CanAuthorize && !HasBotRole;

    public bool CanAddBroadcasterRole => CanAuthorize && !HasBroadcasterRole;

    public StatusSeverity BotBadgeSeverity { get; }

    public StatusSeverity BroadcasterBadgeSeverity { get; }

    private static StatusSeverity MapStatusSeverity(PlatformAuthMethodStatus status) => status switch
    {
        PlatformAuthMethodStatus.Ready => StatusSeverity.Success,
        PlatformAuthMethodStatus.Attention => StatusSeverity.Warning,
        PlatformAuthMethodStatus.Missing => StatusSeverity.Info,
        _ => StatusSeverity.None,
    };

    [RelayCommand(CanExecute = nameof(CanAddBotRole))]
    private Task AddBotRoleAsync() => _addRole(this, PlatformAuthRole.Bot);

    [RelayCommand(CanExecute = nameof(CanAddBroadcasterRole))]
    private Task AddBroadcasterRoleAsync() => _addRole(this, PlatformAuthRole.Broadcaster);

    [RelayCommand]
    private Task SignOutAsync() => _signOut(this);

    [RelayCommand(CanExecute = nameof(CanSignInWebChat))]
    private Task SignInWebChatAsync() => _signInWebChat(this);

    [RelayCommand(CanExecute = nameof(CanResetWebChat))]
    private Task ResetWebChatAsync() => _resetWebChat(this);
}

public sealed record AccountPermissionStatus(string Name, string Status, StatusSeverity Severity);

public sealed partial class PlatformAccountGrantViewModel : ObservableObject
{
    private readonly IPlatformAccountLogin? _login;
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
    [NotifyPropertyChangedFor(nameof(PermissionsLabel))]
    private bool _isPermissionsExpanded;

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
        IPlatformAccountLogin? login,
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
        _login = login;
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

    public bool CanRemove { get; init; }

    public bool SupportsEditableScopes => _login?.SupportsEditableScopes == true;

    public bool SupportsTokenValidation => _login?.SupportsTokenValidation == true;

    public string TokenStatusText { get; }

    public StatusSeverity TokenStatusSeverity { get; }

    public string TokenExpiryText { get; }

    public IReadOnlyList<AccountPermissionStatus> Permissions { get; init; } = [];

    public bool HasPermissions => Permissions.Count > 0;

    public string PermissionsLabel
    {
        get
        {
            if (IsPermissionsExpanded)
            {
                return "Скрыть возможности";
            }

            var available = Permissions.Count(static permission => permission.Severity == StatusSeverity.Success);
            return $"Возможности · подтверждено {available} из {Permissions.Count}";
        }
    }

    [RelayCommand]
    private void TogglePermissions() => IsPermissionsExpanded = !IsPermissionsExpanded;

    public string ExpectedScopesSummary { get; }

    public string GrantedScopesSummary { get; }

    public bool HasScopeMismatch { get; }

    internal string InitialAccessToken => _initialAccessToken;

    internal string InitialRefreshToken => _initialRefreshToken;

    internal bool IsAccessTokenEdited => !string.Equals(AccessTokenValue, _initialAccessToken, StringComparison.Ordinal);

    internal bool IsRefreshTokenEdited => !string.Equals(RefreshTokenValue, _initialRefreshToken, StringComparison.Ordinal);

    internal bool IsScopesEdited => SupportsEditableScopes && !string.Equals(ScopesText, _initialScopesText, StringComparison.Ordinal);

    public bool HasManualEdits => IsAccessTokenEdited || IsRefreshTokenEdited || IsScopesEdited;

    internal void ApplyDraft(string? accessToken, string? refreshToken, string? scopesText)
    {
        if (accessToken is not null)
        {
            AccessTokenValue = accessToken;
        }

        if (refreshToken is not null)
        {
            RefreshTokenValue = refreshToken;
        }

        if (scopesText is not null && SupportsEditableScopes)
        {
            ScopesText = scopesText;
        }
    }

    public string[] GetRequestedScopes()
    {
        var parsed = ScopesText.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _login is not null
            ? _login.MergeRequestedScopes(Role, parsed)
            : parsed;
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
        if (_login is null)
        {
            return;
        }

        ScopesText = string.Join(" ", _login.GetDefaultScopes(Role));
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
