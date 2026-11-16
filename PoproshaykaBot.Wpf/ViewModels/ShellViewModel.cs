using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase, IDisposable
{
    private readonly IBotConnectionController _connectionManager;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly ShellPreferences _preferences;
    private readonly IDialogService _dialogService;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Dictionary<StreamingPlatform, PlatformConnectionItemViewModel> _platformConnectionsById = [];
    private readonly NavigationItem _settingsSection;
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
    private NavigationItem? _current;
    private bool _returningToPage;
    private string? _lastPartialFailureKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectCommand))]
    private BotLifecyclePhase _phase;

    [ObservableProperty]
    private bool _showPlatformMenu;

    public ShellViewModel(
        ModalHostViewModel modal,
        DashboardViewModel overview,
        SettingsPageViewModel settingsPage,
        UserStatisticsPageViewModel statisticsPage,
        StreamHistoryPageViewModel streamHistoryPage,
        CommandsPageViewModel commandsPage,
        LogsViewModel logsPage,
        DiagnosticsViewModel diagnosticsPage,
        ThemeViewModel theme,
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
        IChatPlatformRegistry? platformRegistry = null)
        : base(modal)
    {
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _mainWindow = mainWindow;
        _connectionManager = connectionManager;
        _platformRegistry = platformRegistry;
        _logger = logger;
        _preferences = preferences;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _dialogService = dialogService;
        Theme = theme;
        UpdateBanner = updateBanner;
        OnboardingBanner = onboardingBanner;
        DebugBanner = debugBanner;
        StreamMonitoring = streamMonitoring;

        Overview = overview;
        Overview.UserRequested += OnUserRequested;

        _settingsPage = settingsPage;
        _settingsPage.SettingsSaved += OnSettingsSaved;
        _chatHistory = chatHistory;
        _uiSettings = uiSettings;
        _uiDispatcher = uiDispatcher;
        _uiSettings.WriteFailed += OnUiSettingsWriteFailed;
        _settingsSection = new("Настройки", PackIconLucideKind.Settings, settingsPage, activate: GuardReturn(settingsPage.OnEnter), key: SectionKeys.Settings);

        _statisticsPage = statisticsPage;
        _statisticsSection = new("Пользователи", PackIconLucideKind.Users, statisticsPage, key: SectionKeys.Users) { StartsGroup = true };
        _streamHistorySection = new(
            "История стримов",
            PackIconLucideKind.History,
            streamHistoryPage,
            activate: streamHistoryPage.OnEnter,
            key: SectionKeys.Streams);
        _commandsSection = new("Команды", PackIconLucideKind.Terminal, commandsPage, activate: GuardReturn(commandsPage.OnEnter), key: SectionKeys.Commands);

        _streamHistoryPage = streamHistoryPage;
        _streamHistoryPage.UserRequested += OnUserRequested;

        _overviewSection = new("Обзор", PackIconLucideKind.LayoutDashboard, overview, activate: overview.OnEnter, key: SectionKeys.Overview)
        {
            KeepAlive = true,
        };

        Sections.Add(_overviewSection);
        Sections.Add(_statisticsSection);
        Sections.Add(_streamHistorySection);
        Sections.Add(_commandsSection);
        Sections.Add(new("Логи", PackIconLucideKind.ScrollText, logsPage, key: SectionKeys.Logs) { StartsGroup = true });
        Sections.Add(new("Диагностика", PackIconLucideKind.Gauge, diagnosticsPage, key: SectionKeys.Diagnostics));

        IsNavCollapsed = _preferences.NavCollapsed;

        Selected = FindSectionByKey(_preferences.LastPage) ?? Sections[0];

        InitializePlatformConnections();

        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(OnLifecyclePhaseChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(OnPlatformConnectionChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(statusEvent => SetStatus(statusEvent.Message, StatusSeverity.Info)));

        ApplyPhase(_connectionManager.CurrentPhase);
        RefreshPartialFailureStatus();
    }

    public ThemeViewModel Theme { get; }

    public UpdateBannerViewModel UpdateBanner { get; }

    public OnboardingBannerViewModel OnboardingBanner { get; }

    public DebugBannerViewModel DebugBanner { get; }

    public StreamMonitoringViewModel StreamMonitoring { get; }

    public DashboardViewModel Overview { get; }

    public IReadOnlyList<PlatformConnectionItemViewModel> PlatformConnections { get; private set; } = [];

    public string BuildLabel => AppBuildBadge.Current.Value;

    public string BuildTooltip => AppBuildBadge.Current.Tooltip;

    public bool IsOverviewSelected => ReferenceEquals(Selected, _overviewSection);

    public override IPageHeader? EffectivePageHeader => _preferences.ShowPageHeader ? CurrentPageHeader : null;

    public new Thickness ContentMargin => ReferenceEquals(Selected, _overviewSection) ? default : base.ContentMargin;

    public string ConnectButtonText => Phase switch
    {
        BotLifecyclePhase.Connecting => "Отменить",
        BotLifecyclePhase.Connected => "Отключить",
        BotLifecyclePhase.Disconnecting => "Отключение...",
        _ => "Подключить",
    };

    public override async Task<bool> RequestCloseAsync()
    {
        if (!await ConfirmDirtyPagesOnCloseAsync())
        {
            UpdateBanner.ResetInstallState();

            if (App.IsRestartRequested)
            {
                App.CancelRestart();
                _logger.RestartCancelled();
            }

            return false;
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
        _uiSettings.WriteFailed -= OnUiSettingsWriteFailed;
        _settingsPage.SettingsSaved -= OnSettingsSaved;
        _streamHistoryPage.UserRequested -= OnUserRequested;
        Overview.UserRequested -= OnUserRequested;
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    protected override void NavigateToSettings()
    {
        Selected = _settingsSection;
    }

    [RelayCommand]
    private void NavigateToStatistics()
    {
        Selected = _statisticsSection;
    }

    private void OnUserRequested(object? sender, PoproshaykaBot.Core.Platforms.PlatformUserKey key)
    {
        if (!_statisticsPage.TrySelect(key))
        {
            SetStatus("Этого пользователя нет в статистике", StatusSeverity.Info);
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
            SetStatus("История чата очищена не полностью: часть файлов кэша на диске не удалось обновить", StatusSeverity.Warning);
        }
    }

    public NavigationItem? FindSectionByKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (SectionKeys.LegacyTitles.TryGetValue(key, out var migrated))
        {
            key = migrated;
        }

        if (string.Equals(_settingsSection.Key, key, StringComparison.OrdinalIgnoreCase))
        {
            return _settingsSection;
        }

        return Sections.FirstOrDefault(section => section.Key.Length > 0
            && string.Equals(section.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    protected override void OnNavCollapsedChanged(bool value)
    {
        _preferences.NavCollapsed = value;
    }

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        if (!_returningToPage && !ReferenceEquals(value, _current) && !TryLeaveDirtyPage(value))
        {
            return;
        }

        base.OnSelectionChanged(value);

        OnPropertyChanged(nameof(ContentMargin));
        OnPropertyChanged(nameof(IsOverviewSelected));

        _current = value;

        if (value is not null)
        {
            _preferences.LastPage = value.Key;
        }
    }

    private Action GuardReturn(Action activate)
    {
        return () =>
        {
            if (_returningToPage)
            {
                return;
            }

            activate();
        };
    }

    private static IUnsavedChangesPage? DirtyPageOf(NavigationItem? section)
    {
        return section?.Content is IUnsavedChangesPage { HasUnsavedChanges: true } page ? page : null;
    }

    private IEnumerable<NavigationItem> GatedSections()
    {
        yield return _settingsSection;

        foreach (var section in Sections)
        {
            yield return section;
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
                    await page.TrySaveUnsavedChangesAsync();

                    if (page.HasUnsavedChanges)
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

    private bool TryLeaveDirtyPage(NavigationItem? target)
    {
        if (_current is not { } section || DirtyPageOf(section) is not { } page)
        {
            return true;
        }

        switch (_unsavedChangesPrompt.Ask(page.UnsavedChangesSubject, "переходом"))
        {
            case UnsavedChangesDecision.Save:
                ReturnToPage(section);
                _ = SaveThenNavigateAsync(page, target);
                return false;

            case UnsavedChangesDecision.Discard:
                page.DiscardUnsavedChanges();
                return true;

            default:
                ReturnToPage(section);
                return false;
        }
    }

    private void ReturnToPage(NavigationItem section)
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

    private async Task SaveThenNavigateAsync(IUnsavedChangesPage page, NavigationItem? target)
    {
        await page.TrySaveUnsavedChangesAsync();

        if (page.HasUnsavedChanges || target is null)
        {
            return;
        }

        Selected = target;
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
        if (!_platformConnectionsById.TryGetValue(platform, out var item))
        {
            return;
        }

        switch (item.Phase)
        {
            case BotLifecyclePhase.Connecting:
                _connectionManager.CancelConnection(platform);
                break;

            case BotLifecyclePhase.Connected:
                _logger.PlatformDisconnectRequested(item.DisplayName);

                try
                {
                    await _connectionManager.StopAsync(platform);
                }
                catch (Exception exception)
                {
                    _logger.PlatformDisconnectFailed(exception, item.DisplayName);
                }

                break;

            case BotLifecyclePhase.Disconnecting:
                break;

            default:
                _logger.PlatformConnectRequested(item.DisplayName);

                try
                {
                    _connectionManager.StartConnection(platform);
                }
                catch (InvalidOperationException exception)
                {
                    _logger.PlatformStartConnectionFailed(exception, item.DisplayName);
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
        RefreshPlatformStates();
        RefreshPartialFailureStatus();
    }

    private void OnLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        ApplyPhase(phaseEvent.Phase);
        RefreshPartialFailureStatus();

        if (phaseEvent.Phase == BotLifecyclePhase.Failed)
        {
            _logger.BotConnectFailed(phaseEvent.Exception);

            _dialogService.Error("Ошибка", $"Ошибка подключения бота: {FormatConnectionError(phaseEvent.Exception)}");
        }
    }

    private void OnPlatformConnectionChanged(PlatformConnectionChanged platformEvent)
    {
        if (!_platformConnectionsById.TryGetValue(platformEvent.Platform, out var item))
        {
            return;
        }

        item.IsEnabled = IsPlatformEnabled(platformEvent.Platform);
        item.Phase = platformEvent.Phase;
        item.LastErrorMessage = platformEvent.Phase == BotLifecyclePhase.Failed
            ? FormatConnectionError(platformEvent.Exception)
            : null;

        RefreshPlatformMenuVisibility();
        RefreshPartialFailureStatus();
    }

    private void InitializePlatformConnections()
    {
        var items = new List<PlatformConnectionItemViewModel>();

        foreach (var platform in PlatformVisuals.OrderedPlatforms)
        {
            if (_platformRegistry is not null && _platformRegistry.Find(platform) is null)
            {
                continue;
            }

            var item = new PlatformConnectionItemViewModel(platform)
            {
                IsEnabled = IsPlatformEnabled(platform),
                Phase = _connectionManager.GetPhase(platform),
            };
            items.Add(item);
            _platformConnectionsById[platform] = item;
        }

        PlatformConnections = items;
        RefreshPlatformMenuVisibility();
    }

    private void RefreshPlatformStates()
    {
        foreach (var item in PlatformConnections)
        {
            item.IsEnabled = IsPlatformEnabled(item.Platform);
            item.Phase = _connectionManager.GetPhase(item.Platform);
        }

        RefreshPlatformMenuVisibility();
    }

    private bool IsPlatformEnabled(StreamingPlatform platform)
    {
        if (_platformRegistry?.Find(platform) is { } registered)
        {
            return registered.IsEnabled;
        }

        return platform == StreamingPlatform.Twitch;
    }

    private void RefreshPlatformMenuVisibility()
    {
        ShowPlatformMenu = PlatformConnections.Count(static item =>
            item.IsEnabled || item.Phase is not BotLifecyclePhase.Idle and not BotLifecyclePhase.Disconnected) > 1;
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

        foreach (var item in PlatformConnections)
        {
            if (item.Phase == BotLifecyclePhase.Connected)
            {
                connectedNames.Add(item.DisplayName);
            }
            else if (item.IsEnabled && item.Phase == BotLifecyclePhase.Failed)
            {
                failedNames.Add(item.DisplayName);
                var reason = string.IsNullOrWhiteSpace(item.LastErrorMessage)
                    ? "подробности в журнале"
                    : item.LastErrorMessage;
                failedParts.Add($"{item.DisplayName}: {reason}");
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

    private void SetStatus(string text, StatusSeverity severity)
    {
        StatusText = text;
        StatusSeverity = severity;
    }
}

public sealed partial class PlatformConnectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(StatusLabel), nameof(CanToggle), nameof(MenuHeader), nameof(Tooltip))]
    private BotLifecyclePhase _phase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle), nameof(MenuHeader))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _lastErrorMessage;

    public PlatformConnectionItemViewModel(StreamingPlatform platform)
    {
        Platform = platform;
        DisplayName = PlatformVisuals.GetDisplayName(platform);
    }

    public StreamingPlatform Platform { get; }

    public string DisplayName { get; }

    public string ActionLabel => Phase switch
    {
        BotLifecyclePhase.Connecting => "Отменить",
        BotLifecyclePhase.Connected => "Отключить",
        BotLifecyclePhase.Disconnecting => "Отключение...",
        _ => "Подключить",
    };

    public string StatusLabel => Phase switch
    {
        BotLifecyclePhase.Connecting => "подключение",
        BotLifecyclePhase.Connected => "подключён",
        BotLifecyclePhase.Disconnecting => "отключение",
        BotLifecyclePhase.Cancelled => "отменено",
        BotLifecyclePhase.Failed => "ошибка",
        _ => "отключён",
    };

    public bool CanToggle => Phase != BotLifecyclePhase.Disconnecting
        && (IsEnabled || Phase is BotLifecyclePhase.Connected or BotLifecyclePhase.Connecting);

    public string MenuHeader => $"{DisplayName} ({StatusLabel}) – {ActionLabel}";

    public string Tooltip => string.IsNullOrWhiteSpace(LastErrorMessage)
        ? $"{DisplayName}: {StatusLabel}"
        : $"{DisplayName}: {StatusLabel} – {LastErrorMessage}";
}
