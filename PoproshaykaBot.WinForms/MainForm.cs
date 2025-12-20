using PoproshaykaBot.WinForms.Models;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;
    private readonly BotConnectionManager _connectionManager;
    private readonly UnifiedHttpServer _httpServer;
    private readonly TwitchOAuthService _oauthService;
    private readonly StatisticsCollector _statisticsCollector;
    private readonly UserRankService _userRankService;

    private Bot? _bot;
    private bool _isConnected;
    private UserStatisticsForm? _юзерФорма;

    public MainForm(
        ChatHistoryManager chatHistoryManager,
        UnifiedHttpServer httpServer,
        BotConnectionManager connectionManager,
        SettingsManager settingsManager,
        TwitchOAuthService oauthService,
        StatisticsCollector statisticsCollector,
        UserRankService userRankService)
    {
        _chatHistoryManager = chatHistoryManager;
        _httpServer = httpServer;
        _connectionManager = connectionManager;
        _settingsManager = settingsManager;
        _oauthService = oauthService;
        _statisticsCollector = statisticsCollector;
        _userRankService = userRankService;

        InitializeComponent();

        _connectionManager.ProgressChanged += OnConnectionProgress;
        _connectionManager.ConnectionCompleted += OnConnectionCompleted;

        LoadSettings();
        UpdateBroadcastButtonState();
        InitializePanelVisibility();

        _chatHistoryManager.RegisterChatDisplay(_chatDisplay);

        _httpServer.LogMessage += OnHttpServerLogMessage;
        _settingsManager.ChatSettingsChanged += _httpServer.NotifyChatSettingsChanged;

        AddLogMessage("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");

        KeyPreview = true;

        InitializeWebViewAsync();
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        _connectionManager.CancelConnection();
        _connectionManager.Dispose();



        if (_юзерФорма is { IsDisposed: false })
        {
            _юзерФорма.Close();
            _юзерФорма = null;
        }

        await DisconnectBotAsync();
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.L:
                OnToggleLogsButtonClicked(_toggleLogsButton, EventArgs.Empty);
                return true;

            case Keys.Alt | Keys.C:
                OnToggleChatButtonClicked(_toggleChatButton, EventArgs.Empty);
                return true;



            case Keys.Alt | Keys.U:
                OnOpenUserStatistics();
                return true;

            case Keys.Control | Keys.Shift | Keys.Delete:
                ClearChatHistory();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_httpServer != null)
        {
            try
            {
                _settingsManager.ChatSettingsChanged -= _httpServer.NotifyChatSettingsChanged;

                await _httpServer.DisposeAsync();
            }
            catch (Exception ex)
            {
                AddLogMessage($"Ошибка остановки HTTP сервера: {ex.Message}");
            }
        }

        base.OnFormClosing(e);
    }

    private async void OnConnectButtonClicked(object sender, EventArgs e)
    {
        if (_isConnected == false)
        {
            if (_connectionManager.IsBusy)
            {
                return;
            }

            StartBotConnection();
        }
        else
        {
            if (_connectionManager.IsBusy)
            {
                _connectionManager.CancelConnection();
                AddLogMessage("Отмена подключения...");
            }
            else
            {
                await DisconnectBotAsync();
            }
        }
    }

    private void OnBroadcastButtonClicked(object sender, EventArgs e)
    {
        if (_bot == null)
        {
            return;
        }

        if (_bot.IsBroadcastActive)
        {
            _bot.StopBroadcast();
            UpdateBroadcastButtonState();
            AddLogMessage("Рассылка остановлена.");
        }
        else
        {
            _bot.StartBroadcast();
            UpdateBroadcastButtonState();
            AddLogMessage("Рассылка запущена.");
        }
    }

    private void OnConnectionProgress(object? sender, string message)
    {
        OnBotConnectionProgress(message);
    }

    private void OnConnectionCompleted(object? sender, BotConnectionResult result)
    {
        ShowConnectionProgress(false);

        if (result.IsCancelled)
        {
            AddLogMessage("Подключение отменено пользователем.");
            _connectButton.Text = "Подключить бота";
            _connectButton.BackColor = SystemColors.Control;
        }
        else if (result.IsFailed)
        {
            AddLogMessage($"Ошибка подключения бота: {result.Exception?.Message}");

            MessageBox.Show($"Ошибка подключения бота: {result.Exception?.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

            _connectButton.Text = "Подключить бота";
            _connectButton.BackColor = SystemColors.Control;
        }
        else if (result is { IsSuccess: true, Bot: not null })
        {
            _bot = result.Bot;
            _bot.Connected += OnBotConnected;
            _bot.LogMessage += OnBotLogMessage;
            _bot.ConnectionProgress += OnBotConnectionProgress;
            _bot.StreamStatusChanged += OnStreamStatusChanged;

            _isConnected = true;
            _connectButton.Text = "Отключить бота";
            _connectButton.BackColor = Color.LightGreen;
            UpdateBroadcastButtonState();
            UpdateStreamStatus();
            AddLogMessage("Бот успешно подключен!");
        }
    }

    private void OnBotConnectionProgress(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnBotConnectionProgress), message);
            return;
        }

        _connectionStatusLabel.Text = message;
    }

    private void OnBotConnected(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnBotConnected), message);
            return;
        }

        UpdateBroadcastButtonState();
        UpdateStreamStatus();
    }

    private void OnBotLogMessage(string message)
    {
        AddLogMessage($"[Бот] {message}");
    }

    private void OnToggleLogsButtonClicked(object sender, EventArgs e)
    {
        var settings = _settingsManager.Current;
        settings.Ui.ShowLogsPanel = settings.Ui.ShowLogsPanel == false;
        _settingsManager.SaveSettings(settings);
        UpdatePanelVisibility();
    }

    private void OnToggleChatButtonClicked(object sender, EventArgs e)
    {
        var settings = _settingsManager.Current;
        settings.Ui.ShowChatPanel = settings.Ui.ShowChatPanel == false;
        _settingsManager.SaveSettings(settings);
        UpdatePanelVisibility();
    }

    private void OnSwitchChatViewButtonClicked(object sender, EventArgs e)
    {
        var settings = _settingsManager.Current;
        settings.Ui.CurrentChatViewMode = settings.Ui.CurrentChatViewMode == ChatViewMode.Legacy
            ? ChatViewMode.Overlay
            : ChatViewMode.Legacy;

        _settingsManager.SaveSettings(settings);
        UpdateChatViewMode();
    }

    private void OnOpenUserStatistics()
    {
        if (_юзерФорма == null || _юзерФорма.IsDisposed)
        {
            _юзерФорма = new(_statisticsCollector, _userRankService, _bot);
            _юзерФорма.Show(this);
        }
        else
        {
            _юзерФорма.UpdateBotReference(_bot);
            _юзерФорма.Focus();
        }
    }



    private void OnSettingsButtonClicked(object sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm(_settingsManager, _oauthService, _httpServer);

        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadSettings();
        AddLogMessage("Настройки обновлены.");
    }

    private void OnUserStatisticsButtonClicked(object sender, EventArgs e)
    {
        OnOpenUserStatistics();
    }

    private void OnHttpServerLogMessage(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnHttpServerLogMessage), message);
            return;
        }

        AddLogMessage($"HTTP: {message}");
    }

    private void OnStreamStatusChanged()
    {
        UpdateStreamStatus();
        UpdateStreamInfo();
    }

    private async void OnStreamInfoTimerTick(object? sender, EventArgs e)
    {
        if (_bot == null)
        {
            return;
        }

        if (_bot.StreamStatus == StreamStatus.Online)
        {
            await _bot.RefreshStreamInfoAsync();
            UpdateStreamInfo();
        }
    }

    private void OnOAuthStatusChanged(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnOAuthStatusChanged), message);
            return;
        }

        _connectionStatusLabel.Text = message;
        AddLogMessage($"[OAuth] {message}");
    }

    private void UpdateStreamStatus()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateStreamStatus);
            return;
        }

        if (_bot == null)
        {
            _streamStatusLabel.Text = "Статус стрима: Неизвестен";
            _streamStatusLabel.ForeColor = SystemColors.ControlText;
            _streamInfoLabel.Text = "—";
            return;
        }

        var status = _bot.StreamStatus;

        switch (status)
        {
            case StreamStatus.Online:
                _streamStatusLabel.Text = "🔴 Стрим онлайн";
                _streamStatusLabel.ForeColor = Color.Green;
                UpdateStreamInfo();

                if (_streamInfoTimer.Enabled == false)
                {
                    _streamInfoTimer.Start();
                }

                break;

            case StreamStatus.Offline:
                _streamStatusLabel.Text = "⚫ Стрим офлайн";
                _streamStatusLabel.ForeColor = Color.Gray;
                _streamInfoLabel.Text = "—";

                if (_streamInfoTimer.Enabled)
                {
                    _streamInfoTimer.Stop();
                }

                break;

            case StreamStatus.Unknown:
            default:
                _streamStatusLabel.Text = "Статус стрима: Неизвестен";
                _streamStatusLabel.ForeColor = SystemColors.ControlText;
                _streamInfoLabel.Text = "—";

                if (_streamInfoTimer.Enabled)
                {
                    _streamInfoTimer.Stop();
                }

                break;
        }
    }

    private void UpdateStreamInfo()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateStreamInfo);
            return;
        }

        if (_bot?.CurrentStream == null)
        {
            _streamInfoLabel.Text = "—";
            return;
        }

        var info = _bot.CurrentStream;
        var duration = DateTime.UtcNow - info.StartedAt;
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        var title = string.IsNullOrWhiteSpace(info.Title) ? "Без названия" : info.Title;
        var game = string.IsNullOrWhiteSpace(info.GameName) ? "Без категории" : info.GameName;
        _streamInfoLabel.Text = $"Название: {title} | Игра: {game} | Зрителей: {info.ViewerCount} | В эфире: {hours:0}ч {minutes:00}м";
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
        AddLogMessage("История сообщений чата очищена.");
    }

    private void InitializePanelVisibility()
    {
        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        if (InvokeRequired)
        {
            Invoke(UpdatePanelVisibility);
            return;
        }

        var settings = _settingsManager.Current.Ui;
        var showLogs = settings.ShowLogsPanel;
        var showChat = settings.ShowChatPanel;

        _toggleLogsButton.Text = showLogs ? "Скрыть логи" : "Показать логи";
        _toggleLogsButton.BackColor = showLogs ? Color.LightGreen : SystemColors.Control;

        _toggleChatButton.Text = showChat ? "Скрыть чат" : "Показать чат";
        _toggleChatButton.BackColor = showChat ? Color.LightGreen : SystemColors.Control;

        _contentTableLayoutPanel.ColumnStyles.Clear();

        if (showLogs && showChat)
        {
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
        }
        else if (showLogs && showChat == false)
        {
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F));
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
        }
        else if (showLogs == false && showChat)
        {
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F));
        }
        else
        {
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
            _contentTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
        }

        _logLabel.Visible = showLogs;
        _logTextBox.Visible = showLogs;

        UpdateChatViewMode();

        _contentTableLayoutPanel.PerformLayout();
    }

    private void UpdateChatViewMode()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateChatViewMode);
            return;
        }

        var settings = _settingsManager.Current.Ui;
        var showChat = settings.ShowChatPanel;
        var mode = settings.CurrentChatViewMode;

        if (!showChat)
        {
            _chatDisplay.Visible = false;
            _overlayWebView.Visible = false;
            _switchChatViewButton.Enabled = false;
        }
        else
        {
            _switchChatViewButton.Enabled = true;
            if (mode == ChatViewMode.Legacy)
            {
                _chatDisplay.Visible = true;
                _overlayWebView.Visible = false;
                _switchChatViewButton.Text = "Вид: Чат";
                _switchChatViewButton.BackColor = SystemColors.Control;
            }
            else
            {
                _chatDisplay.Visible = false;
                _overlayWebView.Visible = true;
                _switchChatViewButton.Text = "Вид: Overlay";
                _switchChatViewButton.BackColor = Color.LightBlue;

                if (_overlayWebView.CoreWebView2 == null)
                {
                    InitializeWebViewAsync();
                }
                else
                {
                    UpdateOverlayUrl();
                }
            }
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            await _overlayWebView.EnsureCoreWebView2Async(null);
            UpdateOverlayUrl();
        }
        catch (Exception ex)
        {
            AddLogMessage($"Ошибка инициализации WebView2: {ex.Message}");
        }
    }

    private void UpdateOverlayUrl()
    {
        if (_overlayWebView.CoreWebView2 == null)
        {
            return;
        }

        var port = _settingsManager.Current.Twitch.HttpServerPort;
        var url = $"http://localhost:{port}/chat?preview=true";

        if (_overlayWebView.Source?.ToString() != url)
        {
            _overlayWebView.CoreWebView2.Navigate(url);
        }
    }

    private void StartBotConnection()
    {
        if (_connectionManager.IsBusy)
        {
            return;
        }

        _connectButton.Text = "Отменить";
        _connectButton.BackColor = Color.Orange;
        ShowConnectionProgress(true);

        try
        {
            _connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            AddLogMessage($"Ошибка запуска подключения: {exception.Message}");
            ShowConnectionProgress(false);
            _connectButton.Text = "Подключить бота";
            _connectButton.BackColor = SystemColors.Control;
        }
    }

    private void AddLogMessage(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(AddLogMessage), message);
            return;
        }

        _logTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
        _logTextBox.ScrollToCaret();
    }

    private void UpdateBroadcastButtonState()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateBroadcastButtonState);
            return;
        }

        var isBroadcastActive = _bot is { IsBroadcastActive: true };
        var isConnected = _isConnected;

        if (isConnected == false)
        {
            _broadcastButton.Enabled = false;
            _broadcastButton.Text = "Рассылка недоступна";
            _broadcastButton.BackColor = SystemColors.Control;
        }
        else if (isBroadcastActive)
        {
            _broadcastButton.Enabled = true;
            _broadcastButton.Text = "Остановить рассылку";
            _broadcastButton.BackColor = Color.LightGreen;
        }
        else
        {
            _broadcastButton.Enabled = true;
            _broadcastButton.Text = "Запустить рассылку";
            _broadcastButton.BackColor = SystemColors.Control;
        }
    }

    private void ShowConnectionProgress(bool show)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<bool>(ShowConnectionProgress), show);
            return;
        }

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
        AddLogMessage("Отключение бота...");

        if (_bot != null)
        {
            _bot.Connected -= OnBotConnected;
            _bot.LogMessage -= OnBotLogMessage;
            _bot.ConnectionProgress -= OnBotConnectionProgress;
            _bot.StreamStatusChanged -= OnStreamStatusChanged;

            try
            {
                await _bot.DisconnectAsync();
            }
            catch (Exception exception)
            {
                AddLogMessage($"Ошибка при отключении бота: {exception.Message}");
            }

            await _bot.DisposeAsync();
            _bot = null;
        }

        _isConnected = false;
        _connectButton.Text = "Подключить бота";
        _connectButton.BackColor = SystemColors.Control;
        UpdateBroadcastButtonState();
        UpdateStreamStatus();

        AddLogMessage("Бот отключен.");
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _settingsManager.Current;
            AddLogMessage("Настройки Twitch загружены.");
            UpdatePanelVisibility();
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка загрузки настроек: {exception.Message}");
        }
    }
}
