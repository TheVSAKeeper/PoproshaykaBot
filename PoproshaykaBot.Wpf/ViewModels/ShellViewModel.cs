using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase, IDisposable
{
    private readonly BotConnectionManager _connectionManager;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly ShellPreferences _preferences;
    private readonly IDialogService _dialogService;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly NavigationItem _settingsSection;

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
        ThemeViewModel theme,
        ShellPreferences preferences,
        BotConnectionManager connectionManager,
        IEventBus eventBus,
        ILogger<ShellViewModel> logger,
        IDialogService dialogService)
        : base(modal)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _preferences = preferences;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _dialogService = dialogService;
        Theme = theme;

        _settingsSection = new("Настройки", PackIconLucideKind.Settings, settingsPage, activate: settingsPage.OnEnter);

        Sections.Add(new("Обзор", PackIconLucideKind.LayoutDashboard, overview));
        Sections.Add(_settingsSection);
        Sections.Add(new("Пользователи", PackIconLucideKind.Users, statisticsPage));
        Sections.Add(new("История стримов", PackIconLucideKind.History, streamHistoryPage));

        IsNavCollapsed = _preferences.NavCollapsed;

        var lastPage = _preferences.LastPage;
        Selected = Sections.FirstOrDefault(section => string.Equals(section.Title, lastPage, StringComparison.Ordinal)) ?? Sections[0];

        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(OnLifecyclePhaseChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(statusEvent => SetStatus(statusEvent.Message, StatusSeverity.Info)));

        ApplyPhase(_connectionManager.CurrentPhase);
    }

    public ThemeViewModel Theme { get; }

    public override IPageHeader? EffectivePageHeader => _preferences.ShowPageHeader ? CurrentPageHeader : null;

    public string ConnectButtonText => Phase switch
    {
        BotLifecyclePhase.Connecting => "Отменить",
        BotLifecyclePhase.Connected => "Отключить",
        BotLifecyclePhase.Disconnecting => "Отключение...",
        _ => "Подключить",
    };

    public override async Task<bool> RequestCloseAsync()
    {
        try
        {
            await _connectionManager.ShutdownAsync(BotStopMode.Forced);
        }
        catch (Exception exception)
        {
            _logger.ShutdownOnCloseFailed(exception);
        }

        return true;
    }

    public void Dispose()
    {
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

    protected override void OnNavCollapsedChanged(bool value)
    {
        _preferences.NavCollapsed = value;
    }

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        base.OnSelectionChanged(value);

        if (value is not null)
        {
            _preferences.LastPage = value.Title;
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

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ShellPreferences.ShowPageHeader))
        {
            return;
        }

        OnPropertyChanged(nameof(EffectivePageHeader));
        OnPropertyChanged(nameof(ContentMargin));
    }

    private void OnLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        ApplyPhase(phaseEvent.Phase);

        if (phaseEvent.Phase == BotLifecyclePhase.Failed)
        {
            _logger.BotConnectFailed(phaseEvent.Exception);

            _dialogService.Error("Ошибка", $"Ошибка подключения бота: {phaseEvent.Exception?.Message}");
        }
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
