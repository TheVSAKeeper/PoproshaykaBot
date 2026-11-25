using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public interface IMcpConnectNotificationGate
{
    void SuppressNextErrorDialog();

    bool TryConsumeErrorDialogSuppression();

    void ResetErrorDialogSuppression();
}

public sealed class McpConnectNotificationGate : IMcpConnectNotificationGate
{
    private int _pendingSuppressions;

    public void SuppressNextErrorDialog()
    {
        Interlocked.Increment(ref _pendingSuppressions);
    }

    public bool TryConsumeErrorDialogSuppression()
    {
        return TryDecrement();
    }

    public void ResetErrorDialogSuppression()
    {
        TryDecrement();
    }

    private bool TryDecrement()
    {
        while (true)
        {
            var current = Volatile.Read(ref _pendingSuppressions);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _pendingSuppressions, current - 1, current) == current)
            {
                return true;
            }
        }
    }
}

public sealed partial class ShellViewModel : ShellViewModelBase, IDisposable
{
    private readonly IBotConnectionController _connectionManager;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly ShellPreferences _preferences;
    private readonly IDialogService _dialogService;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Dictionary<StreamingPlatform, string> _platformConnectionErrors = [];
    private readonly NavigationItem _settingsSection;
    private readonly NavigationItem? _multistreamSection;
    private readonly SettingsPageViewModel _settingsPage;
    private readonly ChatHistoryManager _chatHistory;
    private readonly ISettingsStore _uiSettings;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly NavigationItem _statisticsSection;
    private readonly NavigationItem _streamHistorySection;
    private readonly NavigationItem _commandsSection;
    private readonly UserStatisticsPageViewModel _statisticsPage;
    private readonly StreamHistoryPageViewModel _streamHistoryPage;
    private readonly NavigationItem _overviewSection;
    private readonly IUnsavedChangesPrompt _unsavedChangesPrompt;
    private readonly IMainWindowController _mainWindow;
    private readonly UpdateSettingsSectionViewModel? _updateSettings;
    private readonly IMcpConnectNotificationGate _mcpConnectNotificationGate;
    private NavigationItem? _current;
    private bool _returningToPage;
    private bool _leavingPage;
    private bool _closing;
    private string? _lastPartialFailureKey;
    private int _userRequestVersion;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectCommand))]
    private BotLifecyclePhase _phase;

    public ShellViewModel(
        ModalHostViewModel modal,
        DashboardViewModel overview,
        SettingsPageViewModel settingsPage,
        UserStatisticsPageViewModel statisticsPage,
        StreamHistoryPageViewModel streamHistoryPage,
        CommandsPageViewModel commandsPage,
        LogsViewModel logsPage,
        DiagnosticsViewModel diagnosticsPage,
        ThemeSelectionViewModel theme,
        ShellPreferences preferences,
        IBotConnectionController connectionManager,
        IEventBus eventBus,
        ILogger<ShellViewModel> logger,
        IDialogService dialogService,
        UpdateBannerViewModel updateBanner,
        OnboardingBannerViewModel onboardingBanner,
        DebugBannerViewModel debugBanner,
        StreamMonitoringViewModel streamMonitoring,
        ChatHistoryManager chatHistory,
        ISettingsStore uiSettings,
        IUiDispatcher uiDispatcher,
        IUnsavedChangesPrompt unsavedChangesPrompt,
        IMainWindowController mainWindow,
        IChatPlatformRegistry? platformRegistry = null,
        ChannelSwitchPromptBannerViewModel? channelSwitchBanner = null,
        UpdateSettingsSectionViewModel? updateSettings = null,
        MultistreamPageViewModel? multistreamPage = null,
        IMcpConnectNotificationGate? mcpConnectNotificationGate = null,
        ToastHostViewModel? toasts = null)
        : base(modal)
    {
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _mainWindow = mainWindow;
        _updateSettings = updateSettings;
        _mcpConnectNotificationGate = mcpConnectNotificationGate ?? new McpConnectNotificationGate();
        Toasts = toasts ?? new ToastHostViewModel(uiDispatcher);
        _connectionManager = connectionManager;
        _platformRegistry = platformRegistry;
        _logger = logger;
        _preferences = preferences;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _dialogService = dialogService;
        Theme = theme;
        UpdateBanner = updateBanner;
        OnboardingBanner = onboardingBanner;
        ChannelSwitchBanner = channelSwitchBanner;
        DebugBanner = debugBanner;
        StreamMonitoring = streamMonitoring;

        Overview = overview;
        Overview.UserRequested += OnUserRequested;
        Overview.ConnectionToggleRequested += OnOverviewConnectionToggleRequested;
        Overview.PlatformRequested += OnOverviewPlatformRequested;
        Overview.StatusReported += OnOverviewStatusReported;

        _settingsPage = settingsPage;
        _settingsPage.SettingsSaved += OnSettingsSaved;
        _settingsPage.MultistreamRequested += OnMultistreamRequested;
        _chatHistory = chatHistory;
        _uiSettings = uiSettings;
        _uiDispatcher = uiDispatcher;
        _uiSettings.WriteFailed += OnUiSettingsWriteFailed;
        _settingsSection = new("Настройки", PackIconLucideKind.Settings, settingsPage, key: SectionKeys.Settings);

        _statisticsPage = statisticsPage;
        if (_statisticsPage is not null)
        {
            _statisticsPage.StatusReported += OnOverviewStatusReported;
        }

        _statisticsSection = new("Пользователи", PackIconLucideKind.Users, statisticsPage, key: SectionKeys.Users) { StartsGroup = true };
        _streamHistorySection = new(
            "История стримов",
            PackIconLucideKind.History,
            streamHistoryPage,
            key: SectionKeys.Streams);
        _commandsSection = new("Команды", PackIconLucideKind.Terminal, commandsPage, key: SectionKeys.Commands);

        _streamHistoryPage = streamHistoryPage;
        _streamHistoryPage.UserRequested += OnUserRequested;

        _overviewSection = new("Обзор", PackIconLucideKind.LayoutDashboard, overview, key: SectionKeys.Overview)
        {
            KeepAlive = true,
        };

        Sections.Add(_overviewSection);
        if (multistreamPage is not null)
        {
            _multistreamSection = new("Мультистрим", PackIconLucideKind.Layers, multistreamPage, key: SectionKeys.Multistream);
            Sections.Add(_multistreamSection);
        }

        Sections.Add(_statisticsSection);
        Sections.Add(_streamHistorySection);
        Sections.Add(_commandsSection);
        Sections.Add(new("Логи", PackIconLucideKind.ScrollText, logsPage, key: SectionKeys.Logs) { StartsGroup = true });
        Sections.Add(new("Диагностика", PackIconLucideKind.Gauge, diagnosticsPage, key: SectionKeys.Diagnostics));

        IsNavCollapsed = _preferences.NavCollapsed;

        Selected = FindSectionByKey(_preferences.LastPage) ?? Sections[0];

        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(OnLifecyclePhaseChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(OnPlatformConnectionChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(statusEvent => SetStatus(statusEvent.Message, StatusSeverity.Info)));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChannelSwitchFailed>(OnChannelSwitchFailed));

        if (ChannelSwitchBanner is not null)
        {
            ChannelSwitchBanner.StatusReported += OnChannelSwitchBannerStatusReported;
        }

        ApplyPhase(_connectionManager.CurrentPhase);
        RefreshPartialFailureStatus();
    }

    public ThemeSelectionViewModel Theme { get; }

    public ToastHostViewModel Toasts { get; }

    public UpdateBannerViewModel UpdateBanner { get; }

    public OnboardingBannerViewModel OnboardingBanner { get; }

    public ChannelSwitchPromptBannerViewModel? ChannelSwitchBanner { get; }

    public DebugBannerViewModel DebugBanner { get; }

    public StreamMonitoringViewModel StreamMonitoring { get; }

    public DashboardViewModel Overview { get; }


    public string BuildLabel => AppBuildBadge.Current.Value;

    public string BuildTooltip => AppBuildBadge.Current.Tooltip;

    public bool IsOverviewSelected => ReferenceEquals(Selected, _overviewSection);

    public override IPageHeader? EffectivePageHeader => _preferences.ShowPageHeader ? CurrentPageHeader : null;

    public new Thickness ContentMargin => ReferenceEquals(Selected, _overviewSection) ? default : base.ContentMargin;

    public string ConnectButtonText => Phase switch
    {
        BotLifecyclePhase.Connecting => "Отменить",
        BotLifecyclePhase.Connected => "Отключить бота",
        BotLifecyclePhase.Disconnecting => "Отключение...",
        _ => "Подключить бота",
    };

    public string ConnectButtonTooltip
    {
        get
        {
            if (_platformRegistry is null)
            {
                return "Управление ботом на всех включённых площадках";
            }

            var enabled = _platformRegistry.All
                .Where(static platform => platform.IsEnabled)
                .Select(static platform => platform.DisplayName)
                .ToArray();

            return enabled.Length switch
            {
                0 => "Нет включённых площадок для подключения",
                1 => $"Управление ботом на площадке: {enabled[0]}",
                _ => $"Управление ботом на всех включённых площадках: {string.Join(", ", enabled)}",
            };
        }
    }

    public override async Task<bool> RequestCloseAsync()
    {
        if (_disposed || _closing)
        {
            return false;
        }

        _closing = true;
        try
        {
            return await TryCloseAsync();
        }
        finally
        {
            _closing = false;
        }
    }

    private async Task<bool> TryCloseAsync()
    {
        if (_leavingPage || _settingsPage.IsSaving || !await ConfirmDirtyPagesOnCloseAsync())
        {
            UpdateBanner.ResetInstallState();
            _updateSettings?.ResetInstallState();

            if (App.IsRestartRequested)
            {
                App.CancelRestart();
                _logger.RestartCancelled();
            }

            return false;
        }

        await UpdateBanner.CancelInstallAsync();

        if (_updateSettings is not null)
        {
            await _updateSettings.CancelInstallAsync();
        }

        try
        {
            await _connectionManager.StopAsync(BotStopMode.Forced);
        }
        catch (Exception exception)
        {
            _logger.ShutdownOnCloseFailed(exception);
        }

        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _uiSettings.WriteFailed -= OnUiSettingsWriteFailed;
        _settingsPage.SettingsSaved -= OnSettingsSaved;
        _settingsPage.MultistreamRequested -= OnMultistreamRequested;
        _streamHistoryPage.UserRequested -= OnUserRequested;
        Overview.UserRequested -= OnUserRequested;
        Overview.ConnectionToggleRequested -= OnOverviewConnectionToggleRequested;
        Overview.PlatformRequested -= OnOverviewPlatformRequested;
        Overview.StatusReported -= OnOverviewStatusReported;
        if (_statisticsPage is not null)
        {
            _statisticsPage.StatusReported -= OnOverviewStatusReported;
        }
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;

        if (ChannelSwitchBanner is not null)
        {
            ChannelSwitchBanner.StatusReported -= OnChannelSwitchBannerStatusReported;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    protected override IEnumerable<NavigationItem> GetExtraNavigationItems()
    {
        yield return _settingsSection;
    }

    protected override void NavigateToSettings()
    {
        Selected = _settingsSection;
    }

    private void OnOverviewConnectionToggleRequested(object? sender, StreamingPlatform platform)
    {
        if (TogglePlatformCommand.CanExecute(platform))
        {
            TogglePlatformCommand.Execute(platform);
        }
    }

    private void OnOverviewPlatformRequested(object? sender, StreamingPlatform platform) => OpenMultistream(platform, fromOverview: true);

    private void OnOverviewStatusReported(string message, StatusSeverity severity) => SetStatus(message, severity);

    private void OnMultistreamRequested(object? sender, StreamingPlatform? platform) => OpenMultistream(platform);

    private void OpenMultistream(StreamingPlatform? platform, bool fromOverview = false)
    {
        if (_multistreamSection?.Content is not MultistreamPageViewModel page)
        {
            return;
        }

        Action open = platform switch
        {
            null => page.OpenAutomation,
            { } selected when fromOverview => () => page.OpenPlatformFromOverview(selected),
            { } selected => () => page.OpenPlatform(selected),
        };
        if (!TryLeaveDirtyPage(_multistreamSection, open))
        {
            return;
        }

        Navigate(_multistreamSection, open);
    }

    [RelayCommand]
    private void NavigateToStatistics()
    {
        Selected = _statisticsSection;
    }

    private void OnUserRequested(object? sender, PoproshaykaBot.Core.Platforms.PlatformUserKey key)
    {
        if (NavigationBlocked)
        {
            return;
        }

        var version = ++_userRequestVersion;

        if (!_statisticsPage.NeedsPreloadWait)
        {
            SelectUserOrNotify(key);
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => SelectUserAfterPreloadAsync(key, version),
            static exception => Serilog.Log.ForContext<ShellViewModel>()
                .Warning(exception, "Не удалось открыть карточку пользователя после загрузки статистики"));
    }

    private async Task SelectUserAfterPreloadAsync(PoproshaykaBot.Core.Platforms.PlatformUserKey key, int version)
    {
        var found = await _statisticsPage.TrySelectAfterPreloadAsync(key);

        if (NavigationBlocked || version != _userRequestVersion)
        {
            return;
        }

        if (!found)
        {
            SetTransientStatus("Этого пользователя нет в статистике", StatusSeverity.Info, TimeSpan.FromSeconds(4));
            return;
        }

        Selected = _statisticsSection;
    }

    private void SelectUserOrNotify(PoproshaykaBot.Core.Platforms.PlatformUserKey key)
    {
        if (!_statisticsPage.TrySelect(key))
        {
            SetTransientStatus("Этого пользователя нет в статистике", StatusSeverity.Info, TimeSpan.FromSeconds(4));
            return;
        }

        Selected = _statisticsSection;
    }

    [RelayCommand]
    private void NavigateToStreamHistory()
    {
        Selected = _streamHistorySection;
    }

    [RelayCommand]
    private void ClearChatHistory()
    {
        if (!_dialogService.Confirm("Очистка истории чата",
                "Вы уверены, что хотите очистить всю историю сообщений чата?\n\nЭто действие нельзя отменить."))
        {
            return;
        }

        var clearResult = _chatHistory.ClearHistory();
        _logger.ChatHistoryCleared();

        if (!clearResult.IsComplete)
        {
            _logger.ChatHistoryClearIncomplete();
            const string warning = "История чата очищена не полностью: часть файлов кэша на диске не удалось обновить";
            var warningDuration = TimeSpan.FromSeconds(6);
            SetTransientStatus(warning, StatusSeverity.Warning, warningDuration);
            Toasts.ShowIfEnabled(_preferences, warning, StatusSeverity.Warning, warningDuration);
        }
    }

    public NavigationItem? FindSectionByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (SectionKeys.LegacyTitles.TryGetValue(key, out var migrated))
        {
            key = migrated;
        }

        return FindByKey(key);
    }

    protected override void OnNavCollapsedChanged(bool value)
    {
        _preferences.NavCollapsed = value;
    }

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        if (!_returningToPage && !TryLeaveDirtyPage(value))
        {
            return;
        }

        if (!ReferenceEquals(value, Selected))
        {
            Selected = value;
            return;
        }

        _current = value;

        if (value is not null)
        {
            _preferences.LastPage = value.Key;
        }

        if (!_returningToPage)
        {
            ActivatePage(value?.Content);
        }

        base.OnSelectionChanged(value);
        OnPropertyChanged(nameof(ContentMargin));
        OnPropertyChanged(nameof(IsOverviewSelected));
    }

    private static void ActivatePage(object? page)
    {
        switch (page)
        {
            case SettingsPageViewModel settings:
                settings.OnEnter();
                break;
            case MultistreamPageViewModel multistream:
                multistream.OnEnter();
                break;
            case CommandsPageViewModel commands:
                commands.OnEnter();
                break;
            case DashboardViewModel overview:
                overview.OnEnter();
                break;
            case UserStatisticsPageViewModel statistics:
                statistics.OnEnter();
                break;
            case StreamHistoryPageViewModel history:
                history.OnEnter();
                break;
        }
    }

    private static IUnsavedChangesPage? DirtyPageOf(NavigationItem? section)
    {
        return section?.Content is IUnsavedChangesPage { HasUnsavedChanges: true } page ? page : null;
    }

    private IEnumerable<NavigationItem> GatedSections()
    {
        if (_current is { } current)
        {
            yield return current;
        }

        if (_current?.Content is not (SettingsPageViewModel or MultistreamPageViewModel))
        {
            yield return _settingsSection;
        }

        foreach (var section in Sections)
        {
            if (!ReferenceEquals(section, _current) && !ReferenceEquals(section, _multistreamSection))
            {
                yield return section;
            }
        }
    }

    private async Task<bool> ConfirmDirtyPagesOnCloseAsync()
    {
        foreach (var section in GatedSections())
        {
            if (DirtyPageOf(section) is not { } page)
            {
                continue;
            }

            switch (_unsavedChangesPrompt.Ask(page.UnsavedChangesSubject, "закрытием"))
            {
                case UnsavedChangesDecision.Save:
                    var saved = await page.TrySaveUnsavedChangesAsync();
                    if (_disposed)
                    {
                        return false;
                    }

                    if (!saved || page.HasUnsavedChanges)
                    {
                        ReturnToPage(section);
                        return false;
                    }

                    break;

                case UnsavedChangesDecision.Discard:
                    page.DiscardUnsavedChanges();
                    break;

                default:
                    ReturnToPage(section);
                    return false;
            }
        }

        return true;
    }

    private bool TryLeaveDirtyPage(NavigationItem? target, Action? afterNavigation = null)
    {
        if (NavigationBlocked)
        {
            ReturnToPage(_current);
            return false;
        }

        if (ReferenceEquals(_current, target) || _current is not { } section || DirtyPageOf(section) is not { } page)
        {
            return true;
        }

        _leavingPage = true;
        var saving = false;
        try
        {
            var decision = _unsavedChangesPrompt.Ask(page.UnsavedChangesSubject, "переходом");
            if (_disposed)
            {
                ReturnToPage(section);
                return false;
            }

            switch (decision)
            {
                case UnsavedChangesDecision.Save:
                    ReturnToPage(section);
                    saving = true;
                    _ = SaveThenNavigateAsync(section, page, target, afterNavigation);
                    return false;

                case UnsavedChangesDecision.Discard:
                    page.DiscardUnsavedChanges();
                    return true;

                default:
                    ReturnToPage(section);
                    return false;
            }
        }
        finally
        {
            if (!saving)
            {
                _leavingPage = false;
            }
        }
    }

    private bool NavigationBlocked => _disposed || _closing || _leavingPage || _settingsPage.IsSaving;

    private void ReturnToPage(NavigationItem? section)
    {
        _returningToPage = true;

        try
        {
            Selected = section;
        }
        finally
        {
            _returningToPage = false;
        }
    }

    private async Task SaveThenNavigateAsync(
        NavigationItem source, IUnsavedChangesPage page, NavigationItem? target, Action? afterNavigation)
    {
        bool saved;
        try
        {
            saved = await page.TrySaveUnsavedChangesAsync();
        }
        finally
        {
            _leavingPage = false;
        }

        if (NavigationBlocked || !ReferenceEquals(_current, source) || !saved || page.HasUnsavedChanges || target is null)
        {
            return;
        }

        Navigate(target, afterNavigation);
    }

    private void Navigate(NavigationItem target, Action? afterNavigation)
    {
        Selected = target;
        if (!NavigationBlocked && ReferenceEquals(_current, target))
        {
            afterNavigation?.Invoke();
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleConnect))]
    private async Task ToggleConnectAsync()
    {
        switch (Phase)
        {
            case BotLifecyclePhase.Connecting:
                _connectionManager.CancelConnection();
                break;

            case BotLifecyclePhase.Connected:
                _logger.BotDisconnectRequested();

                try
                {
                    await _connectionManager.StopAsync();
                }
                catch (Exception exception)
                {
                    _logger.BotDisconnectFailed(exception);
                }

                break;

            case BotLifecyclePhase.Disconnecting:
                break;

            default:
                _mcpConnectNotificationGate.ResetErrorDialogSuppression();
                _logger.BotConnectRequested();

                try
                {
                    _connectionManager.StartConnection();
                }
                catch (InvalidOperationException exception)
                {
                    _logger.BotStartConnectionFailed(exception);
                }

                break;
        }
    }

    private bool CanToggleConnect()
    {
        return Phase != BotLifecyclePhase.Disconnecting;
    }

    [RelayCommand]
    private async Task TogglePlatformAsync(StreamingPlatform platform)
    {
        if (_platformRegistry?.Find(platform) is not { } registeredPlatform)
        {
            return;
        }

        var platformName = registeredPlatform.DisplayName;
        switch (_connectionManager.GetPhase(platform))
        {
            case BotLifecyclePhase.Connecting:
                _connectionManager.CancelConnection(platform);
                break;

            case BotLifecyclePhase.Connected:
                _logger.PlatformDisconnectRequested(platformName);

                try
                {
                    await _connectionManager.StopAsync(platform);
                }
                catch (Exception exception)
                {
                    _logger.PlatformDisconnectFailed(exception, platformName);
                }

                break;

            case BotLifecyclePhase.Disconnecting:
                break;

            default:
                if (!registeredPlatform.IsEnabled)
                {
                    return;
                }

                _mcpConnectNotificationGate.ResetErrorDialogSuppression();
                _logger.PlatformConnectRequested(platformName);

                try
                {
                    _connectionManager.StartConnection(platform);
                }
                catch (InvalidOperationException exception)
                {
                    _logger.PlatformStartConnectionFailed(exception, platformName);
                }

                break;
        }
    }

    [RelayCommand]
    private void Restart()
    {
        _logger.RestartRequested();

        if (App.IsHeadless)
        {
            _logger.RestartUnavailableHeadless();
            return;
        }

        if (!App.TryRequestRestart())
        {
            return;
        }

        _mainWindow.RequestClose();
    }

    [RelayCommand]
    private void RestartForced()
    {
        _logger.ForcedRestartRequested();

        if (App.IsHeadless)
        {
            _logger.RestartUnavailableHeadless();
            return;
        }

        if (!_dialogService.ConfirmWarning("Принудительный перезапуск",
                "Приложение закроется сразу, без штатной остановки бота.\n\n"
                + "Несохранённые изменения настроек будут потеряны, прощание в чат не отправится, "
                + "положение окна не запомнится.\n\nПерезапустить принудительно?"))
        {
            _logger.ForcedRestartDeclined();
            return;
        }

        if (!App.TryRestartForced())
        {
            _dialogService.Error("Перезапуск не удался",
                "Не получилось запустить приложение заново, поэтому текущее окно осталось открытым. Подробности – в журнале.");
        }
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ShellPreferences.ShowPageHeader))
        {
            return;
        }

        OnPropertyChanged(nameof(EffectivePageHeader));
        OnPropertyChanged(nameof(ContentMargin));
    }

    private void OnUiSettingsWriteFailed(object? sender, SettingsWriteFailedEventArgs e)
    {
        _logger.UiSettingsWriteFailed(e.Exception, e.FilePath);

        _uiDispatcher.Invoke(
            () => SetStatus("Настройки оформления не сохранены на диск, правки применены только в этом сеансе", StatusSeverity.Error));
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        OnboardingBanner.Refresh();
        DebugBanner.Refresh();
        RefreshPartialFailureStatus();
        OnPropertyChanged(nameof(ConnectButtonTooltip));
    }

    public void SuppressNextConnectErrorDialog()
    {
        _mcpConnectNotificationGate.SuppressNextErrorDialog();
    }

    private void OnLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        ApplyPhase(phaseEvent.Phase);
        RefreshPartialFailureStatus();
        OnPropertyChanged(nameof(ConnectButtonTooltip));

        if (phaseEvent.Phase == BotLifecyclePhase.Failed)
        {
            _logger.BotConnectFailed(phaseEvent.Exception);

            if (!_mcpConnectNotificationGate.TryConsumeErrorDialogSuppression())
            {
                _dialogService.Error("Ошибка", $"Ошибка подключения бота: {FormatConnectionError(phaseEvent.Exception)}");
            }
        }
        else if (phaseEvent.Phase is BotLifecyclePhase.Connected or BotLifecyclePhase.Cancelled)
        {
            _mcpConnectNotificationGate.ResetErrorDialogSuppression();
        }
    }

    private void OnPlatformConnectionChanged(PlatformConnectionChanged platformEvent)
    {
        if (platformEvent.Phase == BotLifecyclePhase.Failed)
        {
            _platformConnectionErrors[platformEvent.Platform] = FormatConnectionError(platformEvent.Exception);
        }
        else
        {
            _platformConnectionErrors.Remove(platformEvent.Platform);
        }
        RefreshPartialFailureStatus();
        OnPropertyChanged(nameof(ConnectButtonTooltip));
    }

    private void OnChannelSwitchFailed(ChannelSwitchFailed failed)
    {
        var severity = failed.KeptPreviousChat ? StatusSeverity.Warning : StatusSeverity.Error;
        SetStatus(failed.Reason, severity);
    }

    private void OnChannelSwitchBannerStatusReported(string message, StatusSeverity severity)
    {
        SetStatus(message, severity);
    }

    private void RefreshPartialFailureStatus()
    {
        if (Phase != BotLifecyclePhase.Connected)
        {
            _lastPartialFailureKey = null;
            return;
        }

        var connectedNames = new List<string>();
        var failedParts = new List<string>();
        var failedNames = new List<string>();

        foreach (var platform in _platformRegistry?.All ?? [])
        {
            var displayName = platform.DisplayName;
            var phase = _connectionManager.GetPhase(platform.Id);
            if (phase == BotLifecyclePhase.Connected)
            {
                connectedNames.Add(displayName);
            }
            else if (phase == BotLifecyclePhase.Failed && platform.IsEnabled)
            {
                failedNames.Add(displayName);
                var reason = !_platformConnectionErrors.TryGetValue(platform.Id, out var error) || string.IsNullOrWhiteSpace(error)
                    ? "подробности в журнале"
                    : error;
                failedParts.Add($"{displayName}: {reason}");
            }
        }

        if (failedParts.Count == 0 || connectedNames.Count == 0)
        {
            if (_lastPartialFailureKey is not null)
            {
                _lastPartialFailureKey = null;
                ApplyPhase(Phase);
            }

            return;
        }

        var connectedLabel = string.Join(", ", connectedNames);
        var failedLabel = string.Join(", ", failedNames);
        var status = $"Подключено частично ({connectedLabel}); сбой {string.Join("; ", failedParts)}";
        SetStatus(status, StatusSeverity.Warning);

        if (!string.Equals(_lastPartialFailureKey, status, StringComparison.Ordinal))
        {
            _lastPartialFailureKey = status;
            _logger.BotPartialConnectionFailure(connectedLabel, failedLabel);
        }
    }

    private static string FormatConnectionError(Exception? exception)
    {
        if (exception is AggregateException aggregate)
        {
            exception = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? exception;
        }

        return exception switch
        {
            null => "подробности в журнале",
            OperationCanceledException => "операция отменена",
            TimeoutException => "превышено время ожидания",
            UnauthorizedAccessException => "ошибка авторизации",
            HttpRequestException httpEx => httpEx.StatusCode is { } code
                ? $"сетевая ошибка HTTP {(int)code}"
                : "ошибка сети",
            YouTubeApiException ytEx => ytEx.Kind switch
            {
                YouTubeApiErrorKind.Unauthorized => "требуется повторная авторизация YouTube",
                YouTubeApiErrorKind.Forbidden => "недостаточно прав YouTube",
                YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached => "превышена суточная квота YouTube API",
                YouTubeApiErrorKind.RateLimitExceeded => "слишком много запросов к YouTube, попробуйте позже",
                YouTubeApiErrorKind.LiveChatEnded => "чат трансляции YouTube завершён",
                YouTubeApiErrorKind.NotFound => "канал или трансляция YouTube не найдены",
                YouTubeApiErrorKind.TransportUnsupported => "выбранный транспорт чата YouTube недоступен",
                YouTubeApiErrorKind.Transient => "временный сбой подключения к YouTube",
                _ => "ошибка YouTube API, подробности в журнале",
            },
            HelixRequestException or TwitchAuthorizationMissingException => HelixErrorMessages.SafeMessage(exception),
            _ => "подробности в журнале",
        };
    }

    private void ApplyPhase(BotLifecyclePhase phase)
    {
        Phase = phase;

        var (text, severity) = phase switch
        {
            BotLifecyclePhase.Connecting => ("Подключение...", StatusSeverity.Info),
            BotLifecyclePhase.Connected => ("Бот подключён", StatusSeverity.Success),
            BotLifecyclePhase.Disconnecting => ("Отключение...", StatusSeverity.Info),
            BotLifecyclePhase.Cancelled => ("Подключение отменено", StatusSeverity.Warning),
            BotLifecyclePhase.Failed => ("Ошибка подключения", StatusSeverity.Error),
            _ => ("Бот отключён", StatusSeverity.None),
        };

        SetStatus(text, severity);
    }

    internal void ReportUncleanPreviousRun()
    {
        SetStatus("Прошлый запуск завершился нештатно – сохраните пакет в разделе «Диагностика»", StatusSeverity.Warning);
    }

    private void SetStatus(string text, StatusSeverity severity)
    {
        StatusText = text;
        StatusSeverity = severity;
    }
}
