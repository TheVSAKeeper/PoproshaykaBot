using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private readonly IFormFactory _forms;
    private readonly IEventBus _eventBus;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;
    private readonly BotConnectionManager _connectionManager;
    private readonly ILogger<MainForm> _logger;
    private readonly List<IDisposable> _subs = [];

    private BotLifecyclePhase _currentPhase = BotLifecyclePhase.Idle;
    private bool _shutdownStarted;
    private bool _initialized;
    private UserStatisticsForm? _userStatisticsForm;
    private SettingsForm? _settingsForm;

    public MainForm(
        IServiceProvider services,
        IFormFactory forms,
        IEventBus eventBus,
        ChatHistoryManager chatHistoryManager,
        BotConnectionManager connectionManager,
        SettingsManager settingsManager,
        ILogger<MainForm> logger)
    {
        _forms = forms;
        _eventBus = eventBus;
        _chatHistoryManager = chatHistoryManager;
        _connectionManager = connectionManager;
        _settingsManager = settingsManager;
        _logger = logger;

        InitializeComponent();

        services.HydrateDescendants(this);

        RestoreWindowBounds();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized || DesignMode)
        {
            return;
        }

        _initialized = true;

        Text = $"Попрощайка Бот v{GetDisplayVersion()}";

        _subs.Add(_eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(this, statusEvent => OnBotConnectionProgress(statusEvent.Message)));
        _subs.Add(_eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(this, OnBotLifecyclePhaseChanged));
        _subs.Add(_eventBus.SubscribeOnUi<StreamMonitoringStatusChanged>(this, OnStreamMonitoringStatusChanged));
        _subs.DisposeOnClose(this);

        LoadSettings();
        _logger.LogInformation("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");

        KeyPreview = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (_shutdownStarted || e.Cancel)
        {
            return;
        }

        SaveWindowBounds();

        _shutdownStarted = true;
        e.Cancel = true;
        _ = ShutdownAndCloseAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.U:
                OnOpenUserStatistics();
                return true;

            case Keys.Control | Keys.Shift | Keys.Delete:
                ClearChatHistory();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void OnConnectButtonClicked(object? sender, EventArgs e)
    {
        switch (_currentPhase)
        {
            case BotLifecyclePhase.Idle:
            case BotLifecyclePhase.Cancelled:
            case BotLifecyclePhase.Failed:
            case BotLifecyclePhase.Disconnected:
                StartBotConnection();
                break;

            case BotLifecyclePhase.Connecting:
                _connectionManager.CancelConnection();
                _logger.LogInformation("Отмена подключения...");
                break;

            case BotLifecyclePhase.Connected:
                await DisconnectBotAsync();
                break;

            case BotLifecyclePhase.Disconnecting:
                break;
        }
    }

    private void OnSettingsButtonClicked(object? sender, EventArgs e)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = _forms.Create<SettingsForm>();
        _settingsForm.SettingsApplied += OnSettingsApplied;
        _settingsForm.FormClosed += OnSettingsFormClosed;
        _settingsForm.Show(this);
    }

    private void OnSettingsApplied(object? sender, EventArgs e)
    {
        LoadSettings(reloadDashboard: true);
        _logger.LogInformation("Настройки обновлены.");
    }

    private void OnSettingsFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not SettingsForm form)
        {
            return;
        }

        form.SettingsApplied -= OnSettingsApplied;
        form.FormClosed -= OnSettingsFormClosed;
        form.Dispose();
        _settingsForm = null;
    }

    private void OnUserStatisticsButtonClicked(object? sender, EventArgs e)
    {
        OnOpenUserStatistics();
    }

    private static string GetDisplayVersion()
    {
        var version = Application.ProductVersion;
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);

        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        return version;
    }

    private static bool IsBoundsVisibleOnAnyScreen(Rectangle bounds)
    {
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            if (_userStatisticsForm is { IsDisposed: false })
            {
                _userStatisticsForm.Close();
                _userStatisticsForm = null;
            }

            if (_settingsForm is { IsDisposed: false })
            {
                _settingsForm.Close();
                _settingsForm = null;
            }

            await _connectionManager.ShutdownAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка завершения работы");
        }
        finally
        {
            Close();
        }
    }

    private void OnBotLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        _currentPhase = phaseEvent.Phase;
        ApplyPhaseVisuals(phaseEvent.Phase);

        switch (phaseEvent.Phase)
        {
            case BotLifecyclePhase.Connecting:
                _logger.LogInformation("Подключение бота...");
                break;

            case BotLifecyclePhase.Connected:
                _logger.LogInformation("Бот успешно подключен!");
                break;

            case BotLifecyclePhase.Cancelled:
                _logger.LogInformation("Подключение отменено пользователем.");
                break;

            case BotLifecyclePhase.Failed:
                _logger.LogError(phaseEvent.Exception, "Ошибка подключения бота");

                MessageBox.Show($"Ошибка подключения бота: {phaseEvent.Exception?.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                break;

            case BotLifecyclePhase.Disconnecting:
                _logger.LogInformation("Отключение бота...");
                break;

            case BotLifecyclePhase.Disconnected:
                _logger.LogInformation("Бот отключен.");
                break;
        }
    }

    private void ApplyPhaseVisuals(BotLifecyclePhase phase)
    {
        switch (phase)
        {
            case BotLifecyclePhase.Connecting:
                _connectToolStripButton.Text = "⏹️ Отменить";
                _connectToolStripButton.BackColor = Color.Orange;
                ShowConnectionProgress(true);
                break;

            case BotLifecyclePhase.Connected:
                _connectToolStripButton.Text = "🔌 Отключить";
                _connectToolStripButton.BackColor = Color.LightGreen;
                ShowConnectionProgress(false);
                break;

            case BotLifecyclePhase.Disconnecting:
                _connectToolStripButton.Text = "⏳ Отключение...";
                _connectToolStripButton.BackColor = Color.Orange;
                ShowConnectionProgress(true);
                break;

            case BotLifecyclePhase.Idle:
            case BotLifecyclePhase.Disconnected:
            case BotLifecyclePhase.Cancelled:
            case BotLifecyclePhase.Failed:
                _connectToolStripButton.Text = "🔌 Подключить";
                _connectToolStripButton.BackColor = SystemColors.Control;
                ShowConnectionProgress(false);
                break;
        }
    }

    private void OnBotConnectionProgress(string message)
    {
        _connectionStatusLabel.Text = message;
    }

    private void OnStreamMonitoringStatusChanged(StreamMonitoringStatusChanged statusEvent)
    {
        var (icon, label) = statusEvent.Status switch
        {
            StreamMonitoringStatus.Connecting => ("🟡", "подключение"),
            StreamMonitoringStatus.Connected => ("🟢", "работает"),
            StreamMonitoringStatus.Reconnecting => ("🟡", "переподключение"),
            StreamMonitoringStatus.Failed => ("🔴", "ошибка"),
            _ => ("⚪", "остановлен"),
        };

        var detail = string.IsNullOrWhiteSpace(statusEvent.Detail) ? string.Empty : $" ({statusEvent.Detail})";
        _streamMonitoringStatusLabel.Text = $"{icon} Стрим-мониторинг: {label}{detail}";
    }

    private void OnOpenUserStatistics()
    {
        if (_userStatisticsForm == null || _userStatisticsForm.IsDisposed)
        {
            _userStatisticsForm = _forms.Create<UserStatisticsForm>();
            _userStatisticsForm.Show(this);
        }
        else
        {
            _userStatisticsForm.Focus();
        }
    }

    private void ClearChatHistory()
    {
        var result = MessageBox.Show("Вы уверены, что хотите очистить всю историю сообщений чата?\n\nЭто действие нельзя отменить.",
            "Очистка истории чата",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _chatHistoryManager.ClearHistory();
        _logger.LogInformation("История сообщений чата очищена.");
    }

    private void StartBotConnection()
    {
        try
        {
            _connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "Ошибка запуска подключения");
        }
    }

    private void ShowConnectionProgress(bool show)
    {
        _connectionProgressBar.Visible = show;
        _connectionStatusLabel.Visible = show;

        if (show)
        {
            _connectionProgressBar.Style = ProgressBarStyle.Marquee;
            _connectionStatusLabel.Text = "Подключение...";
        }
        else
        {
            _connectionStatusLabel.Text = "";
        }
    }

    private async Task DisconnectBotAsync()
    {
        if (_currentPhase != BotLifecyclePhase.Connected)
        {
            return;
        }

        try
        {
            await _connectionManager.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка при отключении бота");
        }
    }

    private void RestoreWindowBounds()
    {
        var saved = _settingsManager.Current.Ui.MainWindow;

        if (saved is null || saved.Width <= 0 || saved.Height <= 0)
        {
            return;
        }

        var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);

        if (!IsBoundsVisibleOnAnyScreen(bounds))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;

        if (saved.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowBounds()
    {
        var settings = _settingsManager.Current;
        var isMaximized = WindowState == FormWindowState.Maximized;
        var bounds = isMaximized || WindowState == FormWindowState.Minimized
            ? RestoreBounds
            : Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        settings.Ui.MainWindow = new()
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = isMaximized,
        };

        try
        {
            _settingsManager.SaveSettings(settings);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось сохранить размер окна");
        }
    }

    private void LoadSettings(bool reloadDashboard = false)
    {
        try
        {
            _ = _settingsManager.Current;

            if (reloadDashboard)
            {
                _dashboardControl.ReloadDashboard();
            }

            _logger.LogInformation("Настройки Twitch загружены.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка загрузки настроек");
        }
    }
}
