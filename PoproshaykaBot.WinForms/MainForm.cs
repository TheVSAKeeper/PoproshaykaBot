namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private Bot? _bot;
    private bool _isConnected;
    private BotConnectionManager? _connectionManager;

    public MainForm()
    {
        InitializeComponent();
        InitializeConnectionManager();
        LoadSettings();
        UpdateBroadcastButtonState();
        AddLogMessage("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        _connectionManager?.CancelConnection();
        _connectionManager?.Dispose();

        await DisconnectBotAsync();
        base.OnFormClosed(e);
    }

    private async void OnConnectButtonClicked(object sender, EventArgs e)
    {
        if (_isConnected == false)
        {
            if (_connectionManager?.IsBusy == true)
            {
                return;
            }

            StartBotConnection();
        }
        else
        {
            if (_connectionManager?.IsBusy == true)
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

            _isConnected = true;
            _connectButton.Text = "Отключить бота";
            _connectButton.BackColor = Color.LightGreen;
            UpdateBroadcastButtonState();
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
        AddLogMessage($"[Прогресс] {message}");
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

    private void OnBotConnected(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnBotConnected), message);
            return;
        }

        AddLogMessage($"Успешное подключение: {message}");

        UpdateBroadcastButtonState();
    }

    private void OnBotLogMessage(string message)
    {
        AddLogMessage($"[Бот] {message}");
    }

    private void OnSettingsButtonClicked(object sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm();

        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadSettings();
        AddLogMessage("Настройки обновлены.");
    }

    private static Bot CreateBotWithSettings(string accessToken)
    {
        var settings = SettingsManager.Current.Twitch;
        var statisticsCollector = new StatisticsCollector();

        return new(accessToken, settings, statisticsCollector);
    }

    private void InitializeConnectionManager()
    {
        _connectionManager = new(CreateBotWithSettings, GetAccessTokenAsync);
        _connectionManager.ProgressChanged += OnConnectionProgress;
        _connectionManager.ConnectionCompleted += OnConnectionCompleted;
    }

    private void StartBotConnection()
    {
        if (_connectionManager?.IsBusy == true)
        {
            return;
        }

        _connectButton.Text = "Отменить";
        _connectButton.BackColor = Color.Orange;
        ShowConnectionProgress(true);

        try
        {
            _connectionManager?.StartConnection();
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

        AddLogMessage("Бот отключен.");
    }

    private void LoadSettings()
    {
        try
        {
            var settings = SettingsManager.Current;
            AddLogMessage("Настройки Twitch загружены.");
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка загрузки настроек: {exception.Message}");
        }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var settings = SettingsManager.Current.Twitch;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            MessageBox.Show("OAuth настройки не настроены.\n\n" + "Пожалуйста, настройте ClientId и ClientSecret в настройках приложения.",
                "Настройки OAuth",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.AccessToken) == false)
        {
            if (await TwitchOAuthService.IsTokenValidAsync(settings.AccessToken))
            {
                AddLogMessage("Используется сохраненный токен доступа.");
                return settings.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(settings.RefreshToken) == false)
            {
                try
                {
                    AddLogMessage("Обновление токена доступа...");

                    var validToken = await TwitchOAuthService.GetValidTokenAsync(settings.ClientId,
                        settings.ClientSecret,
                        settings.AccessToken,
                        settings.RefreshToken);

                    AddLogMessage("Токен доступа обновлен.");
                    return validToken;
                }
                catch (Exception exception)
                {
                    AddLogMessage($"Не удалось обновить токен: {exception.Message}");
                }
            }
        }

        TwitchOAuthService.StatusChanged += OnOAuthStatusChanged;

        try
        {
            var oauthTask = TwitchOAuthService.StartOAuthFlowAsync(settings.ClientId,
                settings.ClientSecret,
                settings.Scopes,
                settings.RedirectUri);

            var accessToken = await oauthTask;
            AddLogMessage("OAuth авторизация завершена успешно.");
            return accessToken;
        }
        finally
        {
            TwitchOAuthService.StatusChanged -= OnOAuthStatusChanged;
        }
    }
}
