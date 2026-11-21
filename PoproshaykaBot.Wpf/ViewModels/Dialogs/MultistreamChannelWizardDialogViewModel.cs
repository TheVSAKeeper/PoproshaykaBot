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
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public enum MultistreamChannelWizardMode
{
    None = 0,
    AddChannel = 1,
    AssignAccount = 2,
}

public sealed record WizardChannelTargetOption(
    string ChannelId,
    string DisplayName,
    string Handle,
    string ChannelUrl)
{
    public string Label => string.IsNullOrWhiteSpace(Handle) || string.Equals(Handle, DisplayName, StringComparison.OrdinalIgnoreCase)
        ? DisplayName
        : $"{DisplayName} ({Handle})";
}

public sealed record WizardRoleVerificationItem(
    PlatformAuthRole Role,
    string RoleTitle,
    string AccountName,
    string StatusText,
    string? HintText,
    StatusSeverity Severity,
    bool IsPassed)
{
    public bool HasHint => !string.IsNullOrWhiteSpace(HintText);
}

public sealed partial class MultistreamChannelWizardDialogViewModel : ObservableObject, IDialogViewModel, IAcceptableDialog
{
    private static readonly ChannelAccountOption UnassignedOption =
        new(null, "Не назначен", AuthContextIds.Default, HasRoleGrant: true);

    private readonly ChannelCatalogStore _catalogStore;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly AccountsStore _twitchAccountsStore;
    private readonly YouTubeAccountsStore _youtubeAccountsStore;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly SettingsManager? _settingsManager;
    private readonly ITwitchOAuthService? _twitchOAuth;
    private readonly IYouTubeOAuthService? _youtubeOAuth;
    private readonly IEmbeddedTwitchAuthDialog? _embeddedTwitchAuth;
    private readonly IShellLauncher? _shellLauncher;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IEventBus? _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MultistreamChannelWizardDialogViewModel>? _logger;

    private string _targetChannelHandle = string.Empty;
    private string _targetChannelUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(StepProgressText),
        nameof(StepHeading), nameof(CanShowBack), nameof(PrimaryButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand), nameof(RetryVerificationCommand))]
    private int _currentStep = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(CanShowBack), nameof(ShowChannelSelector))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    private MultistreamChannelWizardMode _mode = MultistreamChannelWizardMode.AddChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthorize), nameof(ChannelInputPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeNewAccountCommand))]
    private StreamingPlatform _platform = StreamingPlatform.Twitch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _platformDisplayName = "Twitch";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _channelInput = string.Empty;

    [ObservableProperty]
    private string _customDisplayName = string.Empty;

    [ObservableProperty]
    private bool _makeActiveChannel = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _targetChannelId = string.Empty;

    [ObservableProperty]
    private string _targetChannelDisplayName = string.Empty;

    [ObservableProperty]
    private WizardChannelTargetOption? _selectedTargetChannel;

    [ObservableProperty]
    private ChannelAccountOption? _selectedBotAccount;

    [ObservableProperty]
    private ChannelAccountOption? _selectedBroadcasterAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand), nameof(AuthorizeNewAccountCommand), nameof(RetryVerificationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepNotice))]
    private string? _stepNotice;

    [ObservableProperty]
    private StatusSeverity _stepNoticeSeverity;

    [ObservableProperty]
    private string _verificationSummaryHeading = string.Empty;

    [ObservableProperty]
    private string _verificationSummaryMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _verificationSummarySeverity;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryVerificationCommand))]
    private bool _hasVerificationFailure;

    public MultistreamChannelWizardDialogViewModel(
        ChannelCatalogStore catalogStore,
        IEnumerable<IPlatformChannelAdapter> adapters,
        AccountsStore twitchAccountsStore,
        YouTubeAccountsStore youtubeAccountsStore,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        SettingsManager? settingsManager = null,
        ITwitchOAuthService? twitchOAuth = null,
        IYouTubeOAuthService? youtubeOAuth = null,
        IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
        IShellLauncher? shellLauncher = null,
        IChatPlatformRegistry? platformRegistry = null,
        IEventBus? eventBus = null,
        TimeProvider? timeProvider = null,
        ILogger<MultistreamChannelWizardDialogViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(twitchAccountsStore);
        ArgumentNullException.ThrowIfNull(youtubeAccountsStore);

        _catalogStore = catalogStore;
        _adapters = adapters.ToDictionary(static adapter => adapter.Platform);
        _twitchAccountsStore = twitchAccountsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _youtubeSettingsStore = youtubeSettingsStore;
        _settingsManager = settingsManager;
        _twitchOAuth = twitchOAuth;
        _youtubeOAuth = youtubeOAuth;
        _embeddedTwitchAuth = embeddedTwitchAuth;
        _shellLauncher = shellLauncher;
        _platformRegistry = platformRegistry;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => Mode == MultistreamChannelWizardMode.AssignAccount
        ? $"Назначение аккаунта · {PlatformDisplayName}"
        : $"Добавление канала · {PlatformDisplayName}";

    public ObservableCollection<WizardChannelTargetOption> AvailableChannels { get; } = [];

    public ObservableCollection<ChannelAccountOption> BotAccountOptions { get; } = [];

    public ObservableCollection<ChannelAccountOption> BroadcasterAccountOptions { get; } = [];

    public ObservableCollection<WizardRoleVerificationItem> VerificationResults { get; } = [];

    public bool HasSavedChanges { get; private set; }

    public bool IsStep1 => CurrentStep == 1;

    public bool IsStep2 => CurrentStep == 2;

    public bool IsStep3 => CurrentStep == 3;

    public string StepProgressText => $"Шаг {CurrentStep} из 3";

    public string StepHeading => CurrentStep switch
    {
        1 => "Выбор канала",
        2 => "Назначение аккаунтов для канала",
        _ => "Автоматическая проверка доступа",
    };

    public string ChannelInputPlaceholder => Platform switch
    {
        StreamingPlatform.YouTube => "@handle, ID канала (UC…) или ссылка на канал",
        StreamingPlatform.Twitch => "логин канала или ссылка twitch.tv/…",
        _ => "логин, ID или ссылка на канал",
    };

    public bool ShowChannelSelector => Mode == MultistreamChannelWizardMode.AssignAccount && AvailableChannels.Count > 1;

    public bool HasAnyAccount => BotAccountOptions.Count > 1 || BroadcasterAccountOptions.Count > 1;

    public bool CanAuthorize => _shellLauncher is not null
        && ((Platform == StreamingPlatform.Twitch && _twitchOAuth is not null)
            || (Platform == StreamingPlatform.YouTube && _youtubeOAuth is not null && _youtubeSettingsStore is not null));

    public bool HasStepNotice => !string.IsNullOrWhiteSpace(StepNotice);

    public bool CanShowBack => Mode == MultistreamChannelWizardMode.AssignAccount ? CurrentStep > 2 : CurrentStep > 1;

    public string PrimaryButtonLabel => CurrentStep switch
    {
        1 => "Далее",
        2 => "Сохранить и проверить",
        _ => "Готово",
    };

    public void InitializeForAdd(StreamingPlatform platform)
    {
        ResetCommon(platform, MultistreamChannelWizardMode.AddChannel);
        MakeActiveChannel = _catalogStore.GetConfiguredChannels(platform).Count == 0;
        CurrentStep = 1;
        RefreshAccountOptions(selectDefaultBotIfEmpty: true);
    }

    public void InitializeForAssignAccount(StreamingPlatform platform, string? channelId = null)
    {
        ResetCommon(platform, MultistreamChannelWizardMode.AssignAccount);
        AvailableChannels.Clear();

        var configured = _catalogStore.GetConfiguredChannels(platform);
        foreach (var entry in configured)
        {
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ChannelId : entry.DisplayName;
            AvailableChannels.Add(new(entry.ChannelId, displayName, entry.Handle, entry.ChannelUrl));
        }

        var activeId = _catalogStore.GetActiveChannel(platform)?.ChannelId;
        var preferredId = !string.IsNullOrWhiteSpace(channelId) ? channelId : activeId;
        var selected = AvailableChannels.FirstOrDefault(option => string.Equals(option.ChannelId, preferredId, StringComparison.Ordinal))
            ?? AvailableChannels.FirstOrDefault();

        OnPropertyChanged(nameof(ShowChannelSelector));
        RefreshAccountOptions(selectDefaultBotIfEmpty: false);
        SelectedTargetChannel = selected;
        if (selected is not null)
        {
            ApplySelectedTargetChannel(selected);
        }

        CurrentStep = 2;
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

    partial void OnSelectedTargetChannelChanged(WizardChannelTargetOption? value)
    {
        if (value is not null && Mode == MultistreamChannelWizardMode.AssignAccount)
        {
            ApplySelectedTargetChannel(value);
        }
    }

    private void ResetCommon(StreamingPlatform platform, MultistreamChannelWizardMode mode)
    {
        Platform = platform;
        Mode = mode;
        PlatformDisplayName = _platformRegistry?.Find(platform)?.DisplayName ?? PlatformVisuals.GetDisplayName(platform);
        HasSavedChanges = false;
        ChannelInput = string.Empty;
        CustomDisplayName = string.Empty;
        TargetChannelId = string.Empty;
        TargetChannelDisplayName = string.Empty;
        _targetChannelHandle = string.Empty;
        _targetChannelUrl = string.Empty;
        AvailableChannels.Clear();
        VerificationResults.Clear();
        StepNotice = null;
        StepNoticeSeverity = StatusSeverity.None;
        HasVerificationFailure = false;
        VerificationSummaryHeading = string.Empty;
        VerificationSummaryMessage = string.Empty;
        VerificationSummarySeverity = StatusSeverity.None;
    }

    private void ApplySelectedTargetChannel(WizardChannelTargetOption option)
    {
        TargetChannelId = option.ChannelId;
        TargetChannelDisplayName = option.DisplayName;
        _targetChannelHandle = option.Handle;
        _targetChannelUrl = option.ChannelUrl;

        var entry = _catalogStore.FindEntry(new(Platform, option.ChannelId));
        var botAssignment = entry?.GetAccountAssignment(PlatformAuthRole.Bot);
        var broadcasterAssignment = entry?.GetAccountAssignment(PlatformAuthRole.Broadcaster);

        SelectedBotAccount = FindMatchingOption(BotAccountOptions, botAssignment?.Account.AccountId, botAssignment?.ContextId)
            ?? BotAccountOptions.FirstOrDefault(static item => !item.IsUnassigned && item.HasRoleGrant)
            ?? UnassignedOption;
        SelectedBroadcasterAccount = FindMatchingOption(BroadcasterAccountOptions, broadcasterAssignment?.Account.AccountId, broadcasterAssignment?.ContextId)
            ?? UnassignedOption;
    }

    private bool CanBack() => !IsBusy && CanShowBack;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        StepNotice = null;
        HasVerificationFailure = false;
        CurrentStep = Math.Max(Mode == MultistreamChannelWizardMode.AssignAccount ? 2 : 1, CurrentStep - 1);
    }

    private bool CanNext()
    {
        if (IsBusy)
        {
            return false;
        }

        return CurrentStep switch
        {
            1 => !string.IsNullOrWhiteSpace(ChannelInput),
            2 => !string.IsNullOrWhiteSpace(TargetChannelId),
            _ => true,
        };
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private async Task NextAsync()
    {
        StepNotice = null;
        if (CurrentStep == 1)
        {
            await ResolveChannelAndAdvanceAsync().ConfigureAwait(true);
            return;
        }

        if (CurrentStep == 2)
        {
            SaveChannelAssignmentImmediately();
            CurrentStep = 3;
            await RunAutoVerificationAsync().ConfigureAwait(true);
            return;
        }

        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, HasSavedChanges);

    private bool CanRetryNow() => !IsBusy && CurrentStep == 3 && HasVerificationFailure;

    [RelayCommand(CanExecute = nameof(CanRetryNow))]
    private Task RetryVerificationAsync() => RunAutoVerificationAsync();

    private bool CanAuthorizeNewAccount() => !IsBusy && CanAuthorize;

    [RelayCommand(CanExecute = nameof(CanAuthorizeNewAccount))]
    private async Task AuthorizeNewAccountAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStepNotice("Ожидаем вход нового аккаунта на площадке…", StatusSeverity.Info);
        _logger?.CombinedAccountAuthorizationStarted(Platform);
        string? signedInAccountId = null;

        try
        {
            signedInAccountId = Platform switch
            {
                StreamingPlatform.Twitch => await AuthorizeTwitchCombinedAsync().ConfigureAwait(true),
                StreamingPlatform.YouTube => await AuthorizeYouTubeCombinedAsync().ConfigureAwait(true),
                _ => null,
            };
        }
        catch (OperationCanceledException)
        {
            SetStepNotice("Вход отменён.", StatusSeverity.Info);
        }
        catch (Exception exception)
        {
            _logger?.ChannelAuthorizationFailed(Platform, PlatformAuthRole.Bot, exception.GetType().Name);
            SetStepNotice($"Не удалось подключить аккаунт. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        if (string.IsNullOrWhiteSpace(signedInAccountId))
        {
            return;
        }

        HasSavedChanges = true;
        RefreshAccountOptions(selectDefaultBotIfEmpty: false);
        var botMatch = BotAccountOptions.FirstOrDefault(option =>
            !option.IsUnassigned && string.Equals(option.AccountId, signedInAccountId, StringComparison.Ordinal));
        if (botMatch is not null)
        {
            SelectedBotAccount = botMatch;
        }

        var broadcasterMatch = BroadcasterAccountOptions.FirstOrDefault(option =>
            !option.IsUnassigned && string.Equals(option.AccountId, signedInAccountId, StringComparison.Ordinal));
        if (broadcasterMatch is not null && (SelectedBroadcasterAccount is null || SelectedBroadcasterAccount.IsUnassigned))
        {
            SelectedBroadcasterAccount = broadcasterMatch;
        }

        SetStepNotice("Аккаунт подключён и выбран для канала.", StatusSeverity.Success);
    }

    private async Task ResolveChannelAndAdvanceAsync()
    {
        var input = ChannelInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            SetStepNotice("Укажите логин, @handle, ID или ссылку на канал.", StatusSeverity.Warning);
            return;
        }

        if (!_adapters.TryGetValue(Platform, out var adapter))
        {
            SetStepNotice("Адаптер площадки не зарегистрирован.", StatusSeverity.Error);
            return;
        }

        IsBusy = true;
        SetStepNotice("Определяем канал на площадке…", StatusSeverity.Info);

        ResolvedChannelIdentity? resolved;
        try
        {
            var preferredContext = ListPlatformAccounts(Platform)
                .SelectMany(static account => account.Authorizations)
                .Where(static grant => grant.HasToken)
                .OrderBy(grant => Platform == StreamingPlatform.YouTube && grant.Role == PlatformAuthRole.Bot ? 0 : 1)
                .FirstOrDefault()?.ContextId;
            resolved = await adapter.ResolveChannelIdentityAsync(input, preferredContext, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.ChannelResolveFailed(exception, Platform, exception.GetType().Name);
            SetStepNotice($"Не удалось определить канал. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
            IsBusy = false;
            return;
        }

        IsBusy = false;
        if (resolved is null || !resolved.Channel.IsValid || resolved.Channel.Platform != Platform)
        {
            SetStepNotice("Не удалось определить канал. Проверьте ввод и авторизацию площадки.", StatusSeverity.Error);
            return;
        }

        TargetChannelId = resolved.Channel.ChannelId;
        TargetChannelDisplayName = !string.IsNullOrWhiteSpace(CustomDisplayName)
            ? CustomDisplayName.Trim()
            : string.IsNullOrWhiteSpace(resolved.DisplayName) ? resolved.Channel.ChannelId : resolved.DisplayName;
        _targetChannelHandle = resolved.Handle;
        _targetChannelUrl = resolved.ChannelUrl;

        StepNotice = null;
        RefreshAccountOptions(selectDefaultBotIfEmpty: true);
        CurrentStep = 2;
    }

    private void SaveChannelAssignmentImmediately()
    {
        var botOption = SelectedBotAccount is { IsUnassigned: false } bot ? bot : null;
        var broadcasterOption = SelectedBroadcasterAccount is { IsUnassigned: false } broadcaster ? broadcaster : null;
        var supportsBackgroundMonitoring = _adapters.TryGetValue(Platform, out var adapter)
            && adapter.Capabilities.HasFlag(PlatformCapabilities.BackgroundStreamMonitoring);

        _catalogStore.Mutate(settings =>
        {
            var existing = settings.Channels.FirstOrDefault(c =>
                c.Platform == Platform && string.Equals(c.ChannelId, TargetChannelId, StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new()
                {
                    Platform = Platform,
                    ChannelId = TargetChannelId,
                    DisplayName = TargetChannelDisplayName,
                    Handle = _targetChannelHandle,
                    ChannelUrl = _targetChannelUrl,
                    KnownAliases = string.IsNullOrWhiteSpace(TargetChannelDisplayName) ? [] : [TargetChannelDisplayName],
                    IsConfigured = true,
                    MonitoringEnabled = supportsBackgroundMonitoring,
                };
                settings.Channels.Add(existing);
            }
            else
            {
                existing.IsConfigured = true;
                if (!string.IsNullOrWhiteSpace(TargetChannelDisplayName))
                {
                    existing.DisplayName = TargetChannelDisplayName;
                }

                if (!string.IsNullOrWhiteSpace(_targetChannelHandle))
                {
                    existing.Handle = _targetChannelHandle;
                }

                if (!string.IsNullOrWhiteSpace(_targetChannelUrl))
                {
                    existing.ChannelUrl = _targetChannelUrl;
                }
            }

            existing.BotAccountId = botOption?.AccountId;
            existing.BotContextId = botOption is null ? AuthContextIds.Default : AuthContextIds.Normalize(botOption.ContextId);
            existing.BotVerifiedSubjectId = null;

            existing.BroadcasterAccountId = broadcasterOption?.AccountId;
            existing.BroadcasterContextId = broadcasterOption is null ? AuthContextIds.Default : AuthContextIds.Normalize(broadcasterOption.ContextId);
            existing.BroadcasterVerifiedSubjectId = null;

            if (Mode == MultistreamChannelWizardMode.AddChannel)
            {
                var routing = settings.Platforms.FirstOrDefault(p => p.Platform == Platform);
                if (routing is null)
                {
                    routing = new() { Platform = Platform };
                    settings.Platforms.Add(routing);
                }

                if (MakeActiveChannel || string.IsNullOrWhiteSpace(routing.ActiveChannelId))
                {
                    routing.ActiveChannelId = TargetChannelId;
                }
            }
        });

        HasSavedChanges = true;
    }

    private async Task RunAutoVerificationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasVerificationFailure = false;
        VerificationResults.Clear();
        VerificationSummarySeverity = StatusSeverity.Info;
        VerificationSummaryHeading = "Проверяем доступ…";
        VerificationSummaryMessage = $"Сверяем права назначенных аккаунтов для канала «{TargetChannelDisplayName}».";

        try
        {
            var botOption = SelectedBotAccount is { IsUnassigned: false } bot ? bot : null;
            var broadcasterOption = SelectedBroadcasterAccount is { IsUnassigned: false } broadcaster ? broadcaster : null;

            if (!_adapters.TryGetValue(Platform, out var adapter))
            {
                VerificationResults.Add(new(
                    PlatformAuthRole.Bot,
                    "Бот",
                    botOption?.DisplayName ?? "Не назначен",
                    "Адаптер площадки не зарегистрирован",
                    "Проверка недоступна без адаптера площадки.",
                    StatusSeverity.Error,
                    IsPassed: false));
                ApplyVerificationSummary();
                return;
            }

            string? verifiedBotId = null;
            string? verifiedBroadcasterId = null;

            if (botOption is null)
            {
                VerificationResults.Add(new(
                    PlatformAuthRole.Bot,
                    "Бот",
                    "Не назначен",
                    "Аккаунт бота не выбран",
                    "Вернитесь на шаг 2 и выберите аккаунт бота или войдите новым аккаунтом.",
                    StatusSeverity.Warning,
                    IsPassed: false));
            }
            else
            {
                var botItem = await VerifyRoleAsync(adapter, PlatformAuthRole.Bot, botOption).ConfigureAwait(true);
                VerificationResults.Add(botItem.Item);
                verifiedBotId = botItem.VerifiedSubjectId;
            }

            if (broadcasterOption is not null)
            {
                var broadcasterItem = await VerifyRoleAsync(adapter, PlatformAuthRole.Broadcaster, broadcasterOption).ConfigureAwait(true);
                VerificationResults.Add(broadcasterItem.Item);
                verifiedBroadcasterId = broadcasterItem.VerifiedSubjectId;
            }

            _catalogStore.Mutate(settings =>
            {
                var entry = settings.Channels.FirstOrDefault(c =>
                    c.Platform == Platform && string.Equals(c.ChannelId, TargetChannelId, StringComparison.Ordinal));
                if (entry is null)
                {
                    return;
                }

                entry.BotVerifiedSubjectId = verifiedBotId;
                entry.BroadcasterVerifiedSubjectId = verifiedBroadcasterId;
            });

            ApplyVerificationSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<(WizardRoleVerificationItem Item, string? VerifiedSubjectId)> VerifyRoleAsync(
        IPlatformChannelAdapter adapter,
        PlatformAuthRole role,
        ChannelAccountOption option)
    {
        var roleTitle = role == PlatformAuthRole.Bot ? "Бот" : "Владелец";
        var key = new ChannelKey(Platform, TargetChannelId);
        var contextId = AuthContextIds.Normalize(option.ContextId);
        _logger?.ChannelRoleVerificationStarted(role, TargetChannelId, Platform);

        ChannelAccessVerification result;
        try
        {
            result = await adapter.VerifyChannelAccessAsync(key, role, contextId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.ChannelRoleVerificationFailed(role, TargetChannelId, Platform, exception.GetType().Name);
            result = new(false, null, StreamingErrorMessages.SafeMessage(exception));
        }

        var passed = result.IsAllowed
            && !string.IsNullOrWhiteSpace(result.SubjectId)
            && string.Equals(result.SubjectId, option.AccountId, StringComparison.Ordinal);

        if (passed)
        {
            return (
                new(role, roleTitle, option.DisplayName, "Доступ подтверждён", null, StatusSeverity.Success, IsPassed: true),
                result.SubjectId);
        }

        _logger?.ChannelRoleVerificationDenied(role, TargetChannelId, Platform);
        var cause = !string.IsNullOrWhiteSpace(result.FailureReason)
            ? result.FailureReason
            : "Площадка не подтвердила доступ назначенного аккаунта.";
        var hint = role == PlatformAuthRole.Bot
            ? "Убедитесь, что аккаунт бота имеет право писать в чат этого канала, или выберите другой аккаунт на шаге 2."
            : "Убедитесь, что выбран аккаунт владельца именно этого канала, или снимите назначение на шаге 2.";

        return (new(role, roleTitle, option.DisplayName, cause, hint, StatusSeverity.Error, IsPassed: false), null);
    }

    private void ApplyVerificationSummary()
    {
        HasVerificationFailure = VerificationResults.Any(static item => !item.IsPassed);
        if (HasVerificationFailure)
        {
            VerificationSummarySeverity = StatusSeverity.Error;
            VerificationSummaryHeading = "Часть проверок не пройдена";
            VerificationSummaryMessage = "Изменения сохранены, но доступ подтверждён не полностью. Исправьте причину ниже и нажмите «Повторить проверку».";
        }
        else
        {
            VerificationSummarySeverity = StatusSeverity.Success;
            VerificationSummaryHeading = "Готово к работе";
            VerificationSummaryMessage = $"Канал «{TargetChannelDisplayName}» сохранён, доступ проверен и подтверждён.";
        }
    }

    private void RefreshAccountOptions(bool selectDefaultBotIfEmpty)
    {
        var previousBotId = SelectedBotAccount?.AccountId;
        var previousBotContext = SelectedBotAccount?.ContextId;
        var previousBroadcasterId = SelectedBroadcasterAccount?.AccountId;
        var previousBroadcasterContext = SelectedBroadcasterAccount?.ContextId;

        var accounts = ListPlatformAccounts(Platform);
        PopulateRoleOptions(BotAccountOptions, accounts, PlatformAuthRole.Bot);
        PopulateRoleOptions(BroadcasterAccountOptions, accounts, PlatformAuthRole.Broadcaster);

        SelectedBotAccount = FindMatchingOption(BotAccountOptions, previousBotId, previousBotContext)
            ?? (selectDefaultBotIfEmpty
                ? BotAccountOptions.FirstOrDefault(static item => !item.IsUnassigned && item.HasRoleGrant) ?? UnassignedOption
                : UnassignedOption);
        SelectedBroadcasterAccount = FindMatchingOption(BroadcasterAccountOptions, previousBroadcasterId, previousBroadcasterContext)
            ?? UnassignedOption;

        OnPropertyChanged(nameof(HasAnyAccount));
    }

    private static void PopulateRoleOptions(
        ObservableCollection<ChannelAccountOption> target,
        IReadOnlyList<PlatformAccount> accounts,
        PlatformAuthRole role)
    {
        target.Clear();
        target.Add(UnassignedOption);

        foreach (var account in accounts)
        {
            var grants = account.Authorizations.Where(g => g.Role == role && g.HasToken).ToArray();
            foreach (var grant in grants)
            {
                var label = grant.IsDefault
                    ? account.DisplayName
                    : $"{account.DisplayName} ({grant.ContextId})";
                target.Add(new(account.Key.AccountId, label, grant.ContextId, HasRoleGrant: true));
            }
        }
    }

    private static ChannelAccountOption? FindMatchingOption(
        IEnumerable<ChannelAccountOption> options,
        string? accountId,
        string? contextId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        var normalizedContext = AuthContextIds.Normalize(contextId);
        return options.FirstOrDefault(option =>
                !option.IsUnassigned
                && string.Equals(option.AccountId, accountId, StringComparison.Ordinal)
                && string.Equals(AuthContextIds.Normalize(option.ContextId), normalizedContext, StringComparison.Ordinal))
            ?? options.FirstOrDefault(option =>
                !option.IsUnassigned && string.Equals(option.AccountId, accountId, StringComparison.Ordinal));
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

    private async Task<string?> AuthorizeTwitchCombinedAsync()
    {
        if (_twitchOAuth is null || _shellLauncher is null)
        {
            return null;
        }

        var twitchSettings = _settingsManager?.Current.Twitch;
        var clientId = twitchSettings?.ClientId ?? string.Empty;
        var clientSecret = twitchSettings?.ClientSecret ?? string.Empty;
        var redirectUri = twitchSettings?.RedirectUri;
        if (_settingsManager is not null && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
        {
            SetStepNotice("Заполните и сохраните ключи приложения Twitch во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return null;
        }

        string[] scopes = [.. TwitchScopes.CombinedDefault];
        OAuthFlowResult? flow = null;
        if (_embeddedTwitchAuth is not null)
        {
            var embedded = await _embeddedTwitchAuth.AuthorizeAsync(new(
                PlatformAuthRole.Bot, clientId, clientSecret, scopes, redirectUri ?? string.Empty, CheckBroadcasterChannel: false)).ConfigureAwait(true);
            if (embedded.Outcome is EmbeddedTwitchAuthOutcome.Canceled or EmbeddedTwitchAuthOutcome.Failed)
            {
                SetStepNotice(embedded.Message, embedded.Outcome == EmbeddedTwitchAuthOutcome.Failed ? StatusSeverity.Error : StatusSeverity.Info);
                return null;
            }

            flow = embedded.Flow;
        }

        flow ??= await _twitchOAuth.StartOAuthFlowToDraftAsync(
            PlatformAuthRole.Bot, clientId, clientSecret, scopes, redirectUri, url => _shellLauncher.Open(url), checkBroadcasterChannel: false).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(flow.UserId))
        {
            SetStepNotice("Twitch не сообщил идентификатор аккаунта.", StatusSeverity.Error);
            return null;
        }

        var userId = flow.UserId.Trim();
        var now = _timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        List<(PlatformAuthRole Role, string ContextId, string Label, TwitchAccountSettings Account)> entries = [];
        foreach (var role in PlatformAuthRoles.Active)
        {
            var required = role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired;
            if (!required.All(scope => grantedScopes.Contains(scope, StringComparer.Ordinal)))
            {
                continue;
            }

            var contextId = ResolveContextForSave(StreamingPlatform.Twitch, role, userId);
            var account = new TwitchAccountSettings();
            flow.ApplyTo(account, now);
            entries.Add((role, contextId, flow.Login, account));
        }

        if (entries.Count == 0)
        {
            SetStepNotice("Twitch не выдал обязательные права.", StatusSeverity.Warning);
            return null;
        }

        _twitchAccountsStore.UpsertContexts(entries);
        foreach (var saved in entries)
        {
            _twitchOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.Twitch, saved.Role, saved.ContextId);
        }

        return userId;
    }

    private async Task<string?> AuthorizeYouTubeCombinedAsync()
    {
        if (_youtubeOAuth is null || _youtubeSettingsStore is null || _shellLauncher is null)
        {
            return null;
        }

        var settings = _youtubeSettingsStore.Load();
        var secret = _youtubeAccountsStore.LoadClientSecret();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            SetStepNotice("Заполните и сохраните ключи Google-проекта во вкладке «Параметры и доступ».", StatusSeverity.Warning);
            return null;
        }

        var flow = await _youtubeOAuth.StartOAuthFlowToDraftAsync(
            PlatformAuthRole.Bot, settings.ClientId, secret, [.. YouTubeScopes.CombinedDefault], url => _shellLauncher.Open(url)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(flow.ChannelId))
        {
            SetStepNotice("YouTube не сообщил идентификатор авторизованного канала.", StatusSeverity.Error);
            return null;
        }

        var channelId = flow.ChannelId.Trim();
        var name = string.IsNullOrWhiteSpace(flow.ChannelTitle) ? channelId : flow.ChannelTitle;
        var now = _timeProvider.GetUtcNow();
        var grantedScopes = flow.Scopes ?? [];
        List<(PlatformAuthRole Role, string ContextId, string Label, YouTubeAccountSettings Account)> entries = [];
        foreach (var role in PlatformAuthRoles.Active)
        {
            if (!YouTubeScopes.ContainsRequiredScopes(role, grantedScopes))
            {
                continue;
            }

            var contextId = ResolveContextForSave(StreamingPlatform.YouTube, role, channelId);
            var account = new YouTubeAccountSettings();
            flow.ApplyTo(account, now);
            entries.Add((role, contextId, name, account));
        }

        if (entries.Count == 0)
        {
            SetStepNotice("YouTube не выдал обязательные права.", StatusSeverity.Warning);
            return null;
        }

        _youtubeAccountsStore.UpsertContexts(entries);
        foreach (var saved in entries)
        {
            _youtubeOAuth.ResetRetryState(saved.Role, saved.ContextId);
            PublishAuthorizationRefreshed(StreamingPlatform.YouTube, saved.Role, saved.ContextId);
        }

        return channelId;
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
}
