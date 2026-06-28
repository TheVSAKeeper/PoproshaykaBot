using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase, IDisposable
{
    private readonly BotConnectionManager _connectionManager;
    private readonly ILogger<ShellViewModel> _logger;
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
        BotConnectionManager connectionManager,
        IEventBus eventBus,
        ILogger<ShellViewModel> logger)
        : base(modal)
    {
        _connectionManager = connectionManager;
        _logger = logger;

        _settingsSection = new("Настройки", PackIconLucideKind.Settings, settingsPage, activate: settingsPage.OnEnter);

        Sections.Add(new("Обзор", PackIconLucideKind.LayoutDashboard, overview));
        Sections.Add(_settingsSection);
        Sections.Add(new("Пользователи", PackIconLucideKind.Users, statisticsPage));
        Sections.Add(new("История стримов", PackIconLucideKind.History, streamHistoryPage));
        Selected = Sections[0];

        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(OnLifecyclePhaseChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(statusEvent => SetStatus(statusEvent.Message, StatusSeverity.Info)));

        ApplyPhase(_connectionManager.CurrentPhase);
    }

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
            _logger.LogError(exception, "Ошибка завершения работы при закрытии окна");
        }

        return true;
    }

    public void Dispose()
    {
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
                    _logger.LogError(exception, "Ошибка при отключении бота");
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
                    _logger.LogError(exception, "Ошибка запуска подключения");
                }

                break;
        }
    }

    private bool CanToggleConnect()
    {
        return Phase != BotLifecyclePhase.Disconnecting;
    }

    private void OnLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        ApplyPhase(phaseEvent.Phase);

        if (phaseEvent.Phase == BotLifecyclePhase.Failed)
        {
            _logger.LogError(phaseEvent.Exception, "Ошибка подключения бота");

            StyledMessageBox.Show($"Ошибка подключения бота: {phaseEvent.Exception?.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
