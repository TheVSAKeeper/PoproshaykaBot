using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed record WizardAccountSummaryItem(
    string AccountId,
    string DisplayName,
    string RolesSummary,
    StatusSeverity Severity);

public sealed partial class MultistreamPlatformSetupDialogViewModel : ObservableObject, IDialogViewModel, IAcceptableDialog
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly AccountsStore _twitchAccountsStore;
    private readonly YouTubeAccountsStore _youtubeAccountsStore;
    private readonly YouTubeSettingsStore _youtubeSettingsStore;
    private readonly ITwitchOAuthService? _twitchOAuth;
    private readonly IYouTubeOAuthService? _youtubeOAuth;
    private readonly IEmbeddedTwitchAuthDialog? _embeddedTwitchAuth;
    private readonly IShellLauncher? _shellLauncher;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ILogger<MultistreamPlatformSetupDialogViewModel>? _logger;
    private readonly SettingsManager? _settingsManager;
    private readonly IEventBus? _eventBus;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(StepProgressText),
        nameof(StepHeading), nameof(CanShowBack), nameof(PrimaryButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand), nameof(RetryVerificationCommand))]
    private int _currentStep = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubePlatform), nameof(PlatformDescription), nameof(CanAuthorize))]
    [NotifyCanExecuteChangedFor(nameof(EnableYouTubeAndContinueCommand), nameof(AuthorizeCombinedCommand),
        nameof(AuthorizeBotCommand), nameof(AuthorizeBroadcasterCommand))]
    private StreamingPlatform _platform = StreamingPlatform.Twitch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(PlatformDescription))]
    private string _platformDisplayName = "Twitch";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuickEnableYouTubeButton))]
    private bool _enableYouTube;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand), nameof(EnableYouTubeAndContinueCommand),
        nameof(AuthorizeCombinedCommand), nameof(AuthorizeBotCommand), nameof(AuthorizeBroadcasterCommand),
        nameof(RetryVerificationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepNotice))]
    private string? _stepNotice;

    [ObservableProperty]
    private StatusSeverity _stepNoticeSeverity;

    [ObservableProperty]
    private string _verificationHeading = string.Empty;

    [ObservableProperty]
    private string _verificationMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _verificationSeverity;

    [ObservableProperty]
    private bool _isVerificationSuccess;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryVerificationCommand))]
    private bool _canRetryVerification;

    public MultistreamPlatformSetupDialogViewModel(
        ChannelCatalogStore catalogStore,
        IEnumerable<IPlatformChannelAdapter> adapters,
        AccountsStore twitchAccountsStore,
        YouTubeAccountsStore youtubeAccountsStore,
        YouTubeSettingsStore youtubeSettingsStore,
        ITwitchOAuthService? twitchOAuth = null,
        IYouTubeOAuthService? youtubeOAuth = null,
        IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
        IShellLauncher? shellLauncher = null,
        IChatPlatformRegistry? platformRegistry = null,
        ILogger<MultistreamPlatformSetupDialogViewModel>? logger = null,
        SettingsManager? settingsManager = null,
        IEventBus? eventBus = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(twitchAccountsStore);
        ArgumentNullException.ThrowIfNull(youtubeAccountsStore);
        ArgumentNullException.ThrowIfNull(youtubeSettingsStore);

        _catalogStore = catalogStore;
        _adapters = adapters.ToDictionary(static adapter => adapter.Platform);
        _twitchAccountsStore = twitchAccountsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _youtubeSettingsStore = youtubeSettingsStore;
        _twitchOAuth = twitchOAuth;
        _youtubeOAuth = youtubeOAuth;
        _embeddedTwitchAuth = embeddedTwitchAuth;
        _shellLauncher = shellLauncher;
        _platformRegistry = platformRegistry;
        _logger = logger;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => $"Мастер настройки · {PlatformDisplayName}";

    public ObservableCollection<WizardAccountSummaryItem> ExistingAccounts { get; } = [];

    public bool HasExistingAccounts => ExistingAccounts.Count > 0;

    public bool HasSavedChanges { get; private set; }

    public bool IsStep1 => CurrentStep == 1;

    public bool IsStep2 => CurrentStep == 2;

    public bool IsStep3 => CurrentStep == 3;

    public string StepProgressText => $"Шаг {CurrentStep} из 3";

    public string StepHeading => CurrentStep switch
    {
        1 => "Подключение площадки",
        2 => "Вход аккаунтом",
        _ => "Проверка доступа и готовность",
    };

    public bool IsYouTubePlatform => Platform == StreamingPlatform.YouTube;

    public bool ShowQuickEnableYouTubeButton => IsYouTubePlatform && !EnableYouTube;

    public string PlatformDescription => Platform switch
    {
        StreamingPlatform.YouTube => "Включение YouTube позволяет отслеживать эфиры, читать и отправлять сообщения в чат канала. Состояние сохраняется сразу при переходе дальше.",
        StreamingPlatform.Twitch => "Площадка Twitch активна по умолчанию. Далее можно подключить аккаунт бота или владельца и сразу проверить доступ к каналу.",
        _ => $"Настройка площадки {PlatformDisplayName}: выберите аккаунт и проверьте доступ к активному каналу.",
    };

    public bool CanAuthorize => _shellLauncher is not null
        && ((Platform == StreamingPlatform.Twitch && _twitchOAuth is not null)
            || (Platform == StreamingPlatform.YouTube && _youtubeOAuth is not null));

    public bool HasStepNotice => !string.IsNullOrWhiteSpace(StepNotice);

    public bool CanShowBack => CurrentStep > 1;

    public string PrimaryButtonLabel => CurrentStep == 3 ? "Готово" : "Далее";

    public void Initialize(StreamingPlatform platform)
    {
        Platform = platform;
        PlatformDisplayName = _platformRegistry?.Find(platform)?.DisplayName ?? PlatformVisuals.GetDisplayName(platform);
        HasSavedChanges = false;
        CurrentStep = 1;
        StepNotice = null;
        StepNoticeSeverity = StatusSeverity.None;
        IsVerificationSuccess = false;
        CanRetryVerification = false;
        VerificationHeading = string.Empty;
        VerificationMessage = string.Empty;
        VerificationSeverity = StatusSeverity.None;

        EnableYouTube = platform != StreamingPlatform.YouTube || _youtubeSettingsStore.Load().Enabled;
        OnPropertyChanged(nameof(ShowQuickEnableYouTubeButton));
        RefreshAccounts();
    }

    public bool TryAccept()
    {
        if (!CanNext())
        {
            return false;
        }

        NextCommand.Execute(null);
        return true;
    }

    private bool CanBack() => !IsBusy && CurrentStep > 1;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        StepNotice = null;
        CanRetryVerification = false;
        CurrentStep = Math.Max(1, CurrentStep - 1);
        if (CurrentStep == 2)
        {
            RefreshAccounts();
        }
    }

    private bool CanNext() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private async Task NextAsync()
    {
        StepNotice = null;
        if (CurrentStep == 1)
        {
            PersistPlatformEnableIfNeeded();
            RefreshAccounts();
            CurrentStep = 2;
            return;
        }

        if (CurrentStep == 2)
        {
            CurrentStep = 3;
            await RunAutoVerificationAsync().ConfigureAwait(true);
            return;
        }

        RequestClose?.Invoke(this, true);
    }

    private bool CanEnableYouTubeAndContinue() => !IsBusy && IsYouTubePlatform;

    [RelayCommand(CanExecute = nameof(CanEnableYouTubeAndContinue))]
    private Task EnableYouTubeAndContinueAsync()
    {
        EnableYouTube = true;
        return NextAsync();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, HasSavedChanges);

    private bool CanStartOAuth() => !IsBusy && CanAuthorize;

    [RelayCommand(CanExecute = nameof(CanStartOAuth))]
    private Task AuthorizeCombinedAsync() => AuthorizeAndVerifyAsync(combined: true, PlatformAuthRole.Bot);

    [RelayCommand(CanExecute = nameof(CanStartOAuth))]
    private Task AuthorizeBotAsync() => AuthorizeAndVerifyAsync(combined: false, PlatformAuthRole.Bot);

    [RelayCommand(CanExecute = nameof(CanStartOAuth))]
    private Task AuthorizeBroadcasterAsync() => AuthorizeAndVerifyAsync(combined: false, PlatformAuthRole.Broadcaster);

    private bool CanRetryNow() => !IsBusy && CurrentStep == 3 && CanRetryVerification;

    [RelayCommand(CanExecute = nameof(CanRetryNow))]
    private Task RetryVerificationAsync() => RunAutoVerificationAsync();

    private void PersistPlatformEnableIfNeeded()
    {
        if (!IsYouTubePlatform)
        {
            return;
        }

        var current = _youtubeSettingsStore.Load();
        if (current.Enabled == EnableYouTube)
        {
            return;
        }

        _youtubeSettingsStore.Mutate(settings => settings.Enabled = EnableYouTube);
        HasSavedChanges = true;
    }

    private async Task AuthorizeAndVerifyAsync(bool combined, PlatformAuthRole role)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStepNotice("Ожидаем завершение входа на площадке…", StatusSeverity.Info);
        if (combined)
        {
            _logger?.CombinedAccountAuthorizationStarted(Platform);
        }
        else
        {
            _logger?.ChannelAuthorizationStarted(Platform, role);
        }

        var succeeded = false;
        try
        {
            succeeded = Platform switch
            {
                StreamingPlatform.Twitch => await AuthorizeTwitchAsync(combined, role).ConfigureAwait(true),
                StreamingPlatform.YouTube => await AuthorizeYouTubeAsync(combined, role).ConfigureAwait(true),
                _ => false,
            };
        }
        catch (OperationCanceledException)
        {
            SetStepNotice("Вход отменён.", StatusSeverity.Info);
        }
        catch (Exception exception)
        {
            _logger?.ChannelAuthorizationFailed(Platform, role, exception.GetType().Name);
            SetStepNotice($"Не удалось выполнить вход. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        if (!succeeded)
        {
            return;
        }

        HasSavedChanges = true;
        RefreshAccounts();
        StepNotice = null;
        CurrentStep = 3;
        await RunAutoVerificationAsync().ConfigureAwait(true);
    }

    private async Task<bool> AuthorizeTwitchAsync(bool combined, PlatformAuthRole role)
    {
        if (_twitchOAuth is null || _shellLauncher is null)
        {
            return false;
        }

        var twitchSettings = _settingsManager?.Current.Twitch;
        var clientId = twitchSettings?.ClientId ?? string.Empty;
        var clientSecret = twitchSettings?.ClientSecret ?? string.Empty;
        var redirectUri = twitchSettings?.RedirectUri;
        if (_settingsManager is not null && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
        {
            SetStepNotice("Заполните и сохраните ключи приложения Twitch во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return false;
        }

        string[] scopes = combined
            ? [.. TwitchScopes.CombinedDefault]
            : role == PlatformAuthRole.Bot
                ? [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional]
                : [.. TwitchScopes.BroadcasterRequired];

        OAuthFlowResult? flow = null;
        if (_embeddedTwitchAuth is not null)
        {
            var embedded = await _embeddedTwitchAuth.AuthorizeAsync(new(
                role, clientId, clientSecret, scopes, redirectUri ?? string.Empty, CheckBroadcasterChannel: false)).ConfigureAwait(true);
            if (embedded.Outcome is EmbeddedTwitchAuthOutcome.Canceled or EmbeddedTwitchAuthOutcome.Failed)
            {
                SetStepNotice(embedded.Message, embedded.Outcome == EmbeddedTwitchAuthOutcome.Failed ? StatusSeverity.Error : StatusSeverity.Info);
                return false;
            }

            flow = embedded.Flow;
        }

        flow ??= await _twitchOAuth.StartOAuthFlowToDraftAsync(
            role, clientId, clientSecret, scopes, redirectUri, url => _shellLauncher.Open(url), checkBroadcasterChannel: false).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(flow.UserId))
        {
            SetStepNotice("Twitch не сообщил идентификатор аккаунта.", StatusSeverity.Error);
            return false;
        }

        var userId = flow.UserId.Trim();
        var now = _timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        List<(PlatformAuthRole Role, string ContextId, string Label, TwitchAccountSettings Account)> entries = [];

        if (combined)
        {
            foreach (var targetRole in PlatformAuthRoles.Active)
            {
                if (!HasRequiredTwitchScopes(targetRole, grantedScopes))
                {
                    continue;
                }

                var contextId = ResolveContextForSave(StreamingPlatform.Twitch, targetRole, userId);
                var account = new TwitchAccountSettings();
                flow.ApplyTo(account, now);
                entries.Add((targetRole, contextId, flow.Login, account));
            }

            if (entries.Count == 0)
            {
                SetStepNotice("Twitch не выдал обязательные права для бота и владельца.", StatusSeverity.Warning);
                return false;
            }
        }
        else
        {
            var contextId = ResolveContextForSave(StreamingPlatform.Twitch, role, userId);
            var account = new TwitchAccountSettings();
            flow.ApplyTo(account, now);
            entries.Add((role, contextId, flow.Login, account));
        }

        _twitchAccountsStore.UpsertContexts(entries);
        foreach (var saved in entries)
        {
            _twitchOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.Twitch, saved.Role, saved.ContextId);
        }

        return true;
    }

    private async Task<bool> AuthorizeYouTubeAsync(bool combined, PlatformAuthRole role)
    {
        if (_youtubeOAuth is null || _shellLauncher is null)
        {
            return false;
        }

        var settings = _youtubeSettingsStore.Load();
        var secret = _youtubeAccountsStore.LoadClientSecret();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            SetStepNotice("Заполните и сохраните ключи Google-проекта во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return false;
        }

        string[] scopes = combined
            ? [.. YouTubeScopes.CombinedDefault]
            : [.. YouTubeScopes.RequiredFor(role)];

        var flow = await _youtubeOAuth.StartOAuthFlowToDraftAsync(
            role, settings.ClientId, secret, scopes, url => _shellLauncher.Open(url)).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(flow.ChannelId))
        {
            SetStepNotice("YouTube не сообщил идентификатор авторизованного канала.", StatusSeverity.Error);
            return false;
        }

        var channelId = flow.ChannelId.Trim();
        var name = string.IsNullOrWhiteSpace(flow.ChannelTitle) ? channelId : flow.ChannelTitle;
        var now = _timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        List<(PlatformAuthRole Role, string ContextId, string Label, YouTubeAccountSettings Account)> entries = [];

        if (combined)
        {
            foreach (var targetRole in PlatformAuthRoles.Active)
            {
                if (!YouTubeScopes.ContainsRequiredScopes(targetRole, grantedScopes))
                {
                    continue;
                }

                var contextId = ResolveContextForSave(StreamingPlatform.YouTube, targetRole, channelId);
                var account = new YouTubeAccountSettings();
                flow.ApplyTo(account, now);
                entries.Add((targetRole, contextId, name, account));
            }

            if (entries.Count == 0)
            {
                SetStepNotice("YouTube не выдал обязательные права для бота и владельца.", StatusSeverity.Warning);
                return false;
            }
        }
        else
        {
            var contextId = ResolveContextForSave(StreamingPlatform.YouTube, role, channelId);
            var account = new YouTubeAccountSettings();
            flow.ApplyTo(account, now);
            entries.Add((role, contextId, name, account));
        }

        _youtubeAccountsStore.UpsertContexts(entries);
        foreach (var saved in entries)
        {
            _youtubeOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.YouTube, saved.Role, saved.ContextId);
        }

        return true;
    }

    private async Task RunAutoVerificationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanRetryVerification = false;
        IsVerificationSuccess = false;
        VerificationSeverity = StatusSeverity.Info;
        VerificationHeading = "Проверяем доступ…";
        VerificationMessage = "Автоматически сверяем права подключённого аккаунта на площадке.";

        try
        {
            var accounts = ListPlatformAccounts(Platform);
            var botGrant = FindBestGrant(accounts, PlatformAuthRole.Bot);
            var configuredChannels = _catalogStore.GetConfiguredChannels(Platform);
            var channel = _catalogStore.GetActiveChannel(Platform)
                ?? (configuredChannels.Count > 0 ? configuredChannels[0] : null);

            if (channel is null)
            {
                if (botGrant is not null)
                {
                    VerificationSeverity = StatusSeverity.Success;
                    VerificationHeading = "Готово к работе";
                    VerificationMessage = $"Аккаунт бота «{botGrant.Value.DisplayName}» подключён. Добавьте канал, чтобы проверить доступ к нему.";
                    IsVerificationSuccess = true;
                    CanRetryVerification = false;
                }
                else
                {
                    VerificationSeverity = StatusSeverity.Warning;
                    VerificationHeading = "Нужен вход аккаунта бота";
                    VerificationMessage = "Вернитесь на шаг 2 и выполните вход аккаунтом бота.";
                    IsVerificationSuccess = false;
                    CanRetryVerification = true;
                }

                return;
            }

            if (!_adapters.TryGetValue(Platform, out var adapter))
            {
                VerificationSeverity = StatusSeverity.Error;
                VerificationHeading = "Адаптер площадки не найден";
                VerificationMessage = "Не удалось выполнить проверку доступа: адаптер площадки не зарегистрирован.";
                IsVerificationSuccess = false;
                CanRetryVerification = true;
                return;
            }

            var targetBotGrant = ResolveChannelRoleGrant(accounts, channel, PlatformAuthRole.Bot) ?? botGrant;
            if (targetBotGrant is null)
            {
                VerificationSeverity = StatusSeverity.Warning;
                VerificationHeading = "Нужен вход аккаунта бота";
                VerificationMessage = "Вернитесь на шаг 2 и войдите аккаунтом бота, чтобы проверить доступ к каналу.";
                IsVerificationSuccess = false;
                CanRetryVerification = true;
                return;
            }

            var channelName = string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.ChannelId : channel.DisplayName;
            _logger?.ChannelRoleVerificationStarted(PlatformAuthRole.Bot, channel.ChannelId, Platform);

            ChannelAccessVerification botResult;
            try
            {
                botResult = await adapter.VerifyChannelAccessAsync(
                    channel.Key, PlatformAuthRole.Bot, targetBotGrant.Value.ContextId, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.ChannelRoleVerificationFailed(PlatformAuthRole.Bot, channel.ChannelId, Platform, exception.GetType().Name);
                botResult = new(false, null, StreamingErrorMessages.SafeMessage(exception));
            }

            var botVerified = botResult.IsAllowed && !string.IsNullOrWhiteSpace(botResult.SubjectId);
            if (!botVerified)
            {
                _logger?.ChannelRoleVerificationDenied(PlatformAuthRole.Bot, channel.ChannelId, Platform);
                PersistVerification(channel.Key, targetBotGrant.Value.AccountId, targetBotGrant.Value.ContextId, verifiedBotId: null,
                    broadcasterAccountId: null, broadcasterContextId: null, verifiedBroadcasterId: null, updateBroadcaster: false);
                VerificationSeverity = StatusSeverity.Error;
                VerificationHeading = "Доступ не подтверждён";
                VerificationMessage = !string.IsNullOrWhiteSpace(botResult.FailureReason)
                    ? botResult.FailureReason
                    : $"Площадка не подтвердила права бота на канале «{channelName}». Проверьте права и повторите проверку.";
                IsVerificationSuccess = false;
                CanRetryVerification = true;
                return;
            }

            var broadcasterGrant = ResolveChannelRoleGrant(accounts, channel, PlatformAuthRole.Broadcaster);
            string? verifiedBroadcasterId = null;
            string? broadcasterFailure = null;
            var updateBroadcaster = false;
            if (broadcasterGrant is not null)
            {
                updateBroadcaster = true;
                _logger?.ChannelRoleVerificationStarted(PlatformAuthRole.Broadcaster, channel.ChannelId, Platform);
                try
                {
                    var broadcasterResult = await adapter.VerifyChannelAccessAsync(
                        channel.Key, PlatformAuthRole.Broadcaster, broadcasterGrant.Value.ContextId, CancellationToken.None).ConfigureAwait(true);
                    if (broadcasterResult.IsAllowed && !string.IsNullOrWhiteSpace(broadcasterResult.SubjectId))
                    {
                        verifiedBroadcasterId = broadcasterResult.SubjectId;
                    }
                    else
                    {
                        _logger?.ChannelRoleVerificationDenied(PlatformAuthRole.Broadcaster, channel.ChannelId, Platform);
                        broadcasterFailure = broadcasterResult.FailureReason ?? "Права владельца канала не подтверждены.";
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger?.ChannelRoleVerificationFailed(PlatformAuthRole.Broadcaster, channel.ChannelId, Platform, exception.GetType().Name);
                    broadcasterFailure = StreamingErrorMessages.SafeMessage(exception);
                }
            }

            PersistVerification(
                channel.Key,
                botResult.SubjectId!,
                targetBotGrant.Value.ContextId,
                botResult.SubjectId!,
                broadcasterGrant?.AccountId,
                broadcasterGrant?.ContextId,
                verifiedBroadcasterId,
                updateBroadcaster);
            HasSavedChanges = true;

            if (!string.IsNullOrWhiteSpace(broadcasterFailure))
            {
                VerificationSeverity = StatusSeverity.Warning;
                VerificationHeading = "Бот готов, но доступ владельца не подтверждён";
                VerificationMessage = broadcasterFailure;
                IsVerificationSuccess = false;
                CanRetryVerification = true;
                return;
            }

            VerificationSeverity = StatusSeverity.Success;
            VerificationHeading = "Готово к работе";
            VerificationMessage = $"Доступ к каналу «{channelName}» подтверждён и сразу сохранён.";
            IsVerificationSuccess = true;
            CanRetryVerification = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PersistVerification(
        ChannelKey key,
        string botAccountId,
        string botContextId,
        string? verifiedBotId,
        string? broadcasterAccountId,
        string? broadcasterContextId,
        string? verifiedBroadcasterId,
        bool updateBroadcaster)
    {
        _catalogStore.Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(c =>
                c.Platform == key.Platform && string.Equals(c.ChannelId, key.ChannelId, StringComparison.Ordinal));
            if (entry is null)
            {
                return;
            }

            entry.BotAccountId = botAccountId;
            entry.BotContextId = AuthContextIds.Normalize(botContextId);
            entry.BotVerifiedSubjectId = verifiedBotId;

            if (updateBroadcaster && !string.IsNullOrWhiteSpace(broadcasterAccountId))
            {
                entry.BroadcasterAccountId = broadcasterAccountId;
                entry.BroadcasterContextId = AuthContextIds.Normalize(broadcasterContextId);
                entry.BroadcasterVerifiedSubjectId = verifiedBroadcasterId;
            }
        });
    }

    private void RefreshAccounts()
    {
        ExistingAccounts.Clear();
        foreach (var account in ListPlatformAccounts(Platform))
        {
            var hasBot = account.Authorizations.Any(static g => g.Role == PlatformAuthRole.Bot && g.HasToken);
            var hasBroadcaster = account.Authorizations.Any(static g => g.Role == PlatformAuthRole.Broadcaster && g.HasToken);
            var (rolesSummary, severity) = (hasBot, hasBroadcaster) switch
            {
                (true, true) => ("Бот и владелец", StatusSeverity.Success),
                (true, false) => ("Бот", StatusSeverity.Success),
                (false, true) => ("Владелец", StatusSeverity.Info),
                _ => ("Требуется повторный вход", StatusSeverity.Warning),
            };
            ExistingAccounts.Add(new(account.Key.AccountId, account.DisplayName, rolesSummary, severity));
        }

        OnPropertyChanged(nameof(HasExistingAccounts));
    }

    private IReadOnlyList<PlatformAccount> ListPlatformAccounts(StreamingPlatform platform)
    {
        var accounts = _adapters.GetValueOrDefault(platform)?.ListAccounts() ?? (platform switch
        {
            StreamingPlatform.Twitch => _twitchAccountsStore.ListAccounts(),
            StreamingPlatform.YouTube => _youtubeAccountsStore.ListAccounts(),
            _ => [],
        });

        return [.. accounts.Where(account => account.Key.Platform == platform)];
    }

    private string ResolveContextForSave(StreamingPlatform platform, PlatformAuthRole role, string accountId)
    {
        var accounts = ListPlatformAccounts(platform);
        var existing = accounts
            .FirstOrDefault(account => string.Equals(account.Key.AccountId, accountId, StringComparison.Ordinal))
            ?.Authorizations
            .FirstOrDefault(grant => grant.Role == role)
            ?.ContextId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AuthContextIds.Normalize(existing);
        }

        var hasAnyDefaultForRole = accounts
            .SelectMany(static account => account.Authorizations)
            .Any(grant => grant.Role == role && grant.IsDefault && grant.HasToken);
        if (!hasAnyDefaultForRole)
        {
            return AuthContextIds.Default;
        }

        var prefix = platform == StreamingPlatform.Twitch ? "tw" : "yt";
        return $"{prefix}-{role.ToString().ToLowerInvariant()}-{accountId}";
    }

    private void PublishAuthorizationRefreshed(StreamingPlatform platform, PlatformAuthRole role, string contextId)
    {
        if (_eventBus is not null)
        {
            _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(platform, role, AuthContextIds.Normalize(contextId)));
        }
    }

    private void SetStepNotice(string message, StatusSeverity severity)
    {
        StepNoticeSeverity = severity;
        StepNotice = message;
    }

    private static (string AccountId, string DisplayName, string ContextId)? FindBestGrant(
        IReadOnlyList<PlatformAccount> accounts, PlatformAuthRole role)
    {
        foreach (var account in accounts)
        {
            var grant = account.Authorizations.FirstOrDefault(g => g.Role == role && g.HasToken);
            if (grant is not null)
            {
                return (account.Key.AccountId, account.DisplayName, grant.ContextId);
            }
        }

        return null;
    }

    private static (string AccountId, string DisplayName, string ContextId)? ResolveChannelRoleGrant(
        IReadOnlyList<PlatformAccount> accounts, ChannelCatalogEntry channel, PlatformAuthRole role)
    {
        var assignment = channel.GetAccountAssignment(role);
        if (assignment is null)
        {
            return null;
        }

        var account = accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Key.AccountId, assignment.Account.AccountId, StringComparison.Ordinal));
        if (account is null)
        {
            return null;
        }

        var grant = account.Authorizations.FirstOrDefault(g =>
                g.Role == role && string.Equals(g.ContextId, assignment.ContextId, StringComparison.Ordinal) && g.HasToken)
            ?? account.Authorizations.FirstOrDefault(g => g.Role == role && g.HasToken);
        return grant is null ? null : (account.Key.AccountId, account.DisplayName, grant.ContextId);
    }

    private static bool HasRequiredTwitchScopes(PlatformAuthRole role, IReadOnlyCollection<string> grantedScopes)
    {
        var required = role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired;
        var set = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        return required.All(set.Contains);
    }
}
