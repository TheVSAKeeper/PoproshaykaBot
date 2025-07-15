namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private Bot? _bot;
    private bool _isConnected;
    private CancellationTokenSource? _connectionCancellationTokenSource;

    public MainForm()
    {
        InitializeComponent();
        LoadSettings();
        AddLogMessage("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _connectionCancellationTokenSource?.Cancel();
        _connectionCancellationTokenSource?.Dispose();

        DisconnectBot();
        base.OnFormClosed(e);
    }

    private async void OnConnectButtonClicked(object sender, EventArgs e)
    {
        if (_isConnected == false)
        {
            await ConnectBotAsync();
        }
        else
        {
            if (_connectionCancellationTokenSource != null)
            {
                await _connectionCancellationTokenSource.CancelAsync();
                AddLogMessage("Отмена подключения...");
            }
            else
            {
                DisconnectBot();
            }
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
        return new(accessToken, settings);
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

    private async Task ConnectBotAsync()
    {
        string accessToken;

        try
        {
            var token = await GetAccessTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                AddLogMessage("Подключение отменено пользователем.");
                return;
            }

            accessToken = token;
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка авторизации: {exception.Message}");

            MessageBox.Show($"Ошибка авторизации: {exception.Message}", "Ошибка авторизации",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

            return;
        }

        _connectionCancellationTokenSource = new();
        var cancellationToken = _connectionCancellationTokenSource.Token;

        ShowConnectionProgress(true);
        _connectButton.Text = "Отменить";
        _connectButton.BackColor = Color.Orange;

        try
        {
            AddLogMessage("Создание экземпляра бота...");
            _bot = CreateBotWithSettings(accessToken);

            _bot.Connected += OnBotConnected;
            _bot.LogMessage += OnBotLogMessage;
            _bot.ConnectionProgress += OnBotConnectionProgress;

            AddLogMessage("Начало подключения к Twitch...");
            await _bot.ConnectAsync(cancellationToken);

            _isConnected = true;
            _connectButton.Text = "Отключить бота";
            _connectButton.BackColor = Color.LightGreen;
            AddLogMessage("Бот успешно подключен!");
        }
        catch (OperationCanceledException)
        {
            AddLogMessage("Подключение отменено пользователем.");
            CleanupAfterConnectionFailure();
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка подключения бота: {exception.Message}");

            MessageBox.Show($"Ошибка подключения бота: {exception.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

            CleanupAfterConnectionFailure();
        }
        finally
        {
            ShowConnectionProgress(false);
            _connectionCancellationTokenSource?.Dispose();
            _connectionCancellationTokenSource = null;
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

    private void CleanupAfterConnectionFailure()
    {
        if (InvokeRequired)
        {
            Invoke(CleanupAfterConnectionFailure);
            return;
        }

        _isConnected = false;
        _connectButton.Text = "Подключить бота";
        _connectButton.BackColor = SystemColors.Control;

        if (_bot == null)
        {
            return;
        }

        _bot.Connected -= OnBotConnected;
        _bot.LogMessage -= OnBotLogMessage;
        _bot.ConnectionProgress -= OnBotConnectionProgress;
        _bot.Dispose();
        _bot = null;
    }

    private void DisconnectBot()
    {
        AddLogMessage("Отключение бота...");

        if (_bot != null)
        {
            _bot.Connected -= OnBotConnected;
            _bot.LogMessage -= OnBotLogMessage;
            _bot.ConnectionProgress -= OnBotConnectionProgress;
            _bot.Dispose();
            _bot = null;
        }

        _isConnected = false;
        _connectButton.Text = "Подключить бота";
        _connectButton.BackColor = SystemColors.Control;

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
