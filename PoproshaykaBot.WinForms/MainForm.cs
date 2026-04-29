using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private readonly IFormFactory _forms;
    private readonly IEventBus _eventBus;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;
    private readonly BotConnectionManager _connectionManager;
    private readonly List<IDisposable> _subs = [];

    private BotLifecyclePhase _currentPhase = BotLifecyclePhase.Idle;
    private bool _shutdownStarted;
    private bool _initialized;
    private UserStatisticsForm? _userStatisticsForm;

    public MainForm(
        IServiceProvider services,
        IFormFactory forms,
        IEventBus eventBus,
        ChatHistoryManager chatHistoryManager,
        BotConnectionManager connectionManager,
        SettingsManager settingsManager)
    {
        _forms = forms;
        _eventBus = eventBus;
        _chatHistoryManager = chatHistoryManager;
        _connectionManager = connectionManager;
        _settingsManager = settingsManager;

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
        _subs.DisposeOnClose(this);

        LoadSettings();
        PublishLogMessage("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");

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
                PublishLogMessage("Отмена подключения...");
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
        using var settingsForm = _forms.Create<SettingsForm>();

        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadSettings(reloadDashboard: true);
        PublishLogMessage("Настройки обновлены.");
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

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            _connectionManager.CancelConnection();

            if (_userStatisticsForm is { IsDisposed: false })
            {
                _userStatisticsForm.Close();
                _userStatisticsForm = null;
            }

            await DisconnectBotAsync();
        }
        catch (Exception ex)
        {
            PublishLogMessage($"Ошибка завершения работы: {ex.Message}", BotLogLevel.Error);
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
                PublishLogMessage("Подключение бота...");
                break;

            case BotLifecyclePhase.Connected:
                PublishLogMessage("Бот успешно подключен!");
                break;

            case BotLifecyclePhase.Cancelled:
                PublishLogMessage("Подключение отменено пользователем.");
                break;

            case BotLifecyclePhase.Failed:
                PublishLogMessage($"Ошибка подключения бота: {phaseEvent.Exception?.Message}", BotLogLevel.Error);

                MessageBox.Show($"Ошибка подключения бота: {phaseEvent.Exception?.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                break;

            case BotLifecyclePhase.Disconnecting:
                PublishLogMessage("Отключение бота...");
                break;

            case BotLifecyclePhase.Disconnected:
                PublishLogMessage("Бот отключен.");
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
        PublishLogMessage("История сообщений чата очищена.");
    }

    private void StartBotConnection()
    {
        try
        {
            _connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            PublishLogMessage($"Ошибка запуска подключения: {exception.Message}", BotLogLevel.Error);
        }
    }

    private void PublishLogMessage(string message, BotLogLevel level = BotLogLevel.Information, string source = "Ui")
    {
        _ = _eventBus.PublishAsync(new BotLogEntry(level, source, message));
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
            PublishLogMessage($"Ошибка при отключении бота: {exception.Message}", BotLogLevel.Error);
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

        settings.Ui.MainWindow = new MainWindowSettings
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
            PublishLogMessage($"Не удалось сохранить размер окна: {exception.Message}", BotLogLevel.Error);
        }
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

    private void LoadSettings(bool reloadDashboard = false)
    {
        try
        {
            _ = _settingsManager.Current;

            if (reloadDashboard)
            {
                _dashboardControl.ReloadDashboard();
            }

            PublishLogMessage("Настройки Twitch загружены.");
        }
        catch (Exception exception)
        {
            PublishLogMessage($"Ошибка загрузки настроек: {exception.Message}", BotLogLevel.Error);
        }
    }
}
