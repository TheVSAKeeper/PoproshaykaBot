namespace PoproshaykaBot.WinForms;

public partial class SettingsForm : Form
{
    private static readonly TwitchSettings DefaultSettings = new();
    private AppSettings _settings;
    private bool _hasChanges;
    private bool _tokensVisible;

    public SettingsForm()
    {
        _settings = new();
        CopySettings(SettingsManager.Current, _settings);
        InitializeComponent();
        SetPlaceholders();
        LoadSettingsToControls();
        LoadTokenInformation();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        _hasChanges = true;
        UpdateButtonStates();
    }

    private void OnOkButtonClicked(object sender, EventArgs e)
    {
        if (_hasChanges)
        {
            ApplySettings();
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelButtonClicked(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnApplyButtonClicked(object sender, EventArgs e)
    {
        ApplySettings();
    }

    private void OnResetButtonClicked(object sender, EventArgs e)
    {
        var result = MessageBox.Show("Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?",
            "Подтверждение сброса",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _settings = new();
        LoadSettingsToControls();
        _hasChanges = true;
        UpdateButtonStates();
    }

    private void OnBotUsernameResetButtonClicked(object sender, EventArgs e)
    {
        _botUsernameTextBox.Text = DefaultSettings.BotUsername;
    }

    private void OnChannelResetButtonClicked(object sender, EventArgs e)
    {
        _channelTextBox.Text = DefaultSettings.Channel;
    }

    private void OnMessagesAllowedResetButtonClicked(object sender, EventArgs e)
    {
        _messagesAllowedNumeric.Value = DefaultSettings.MessagesAllowedInPeriod;
    }

    private void OnThrottlingPeriodResetButtonClicked(object sender, EventArgs e)
    {
        _throttlingPeriodNumeric.Value = DefaultSettings.ThrottlingPeriodSeconds;
    }

    private void OnClientIdResetButtonClicked(object sender, EventArgs e)
    {
        _clientIdTextBox.Text = DefaultSettings.ClientId;
    }

    private void OnClientSecretResetButtonClicked(object sender, EventArgs e)
    {
        _clientSecretTextBox.Text = DefaultSettings.ClientSecret;
    }

    private void OnRedirectUriResetButtonClicked(object sender, EventArgs e)
    {
        _redirectUriTextBox.Text = DefaultSettings.RedirectUri;
    }

    private void OnScopesResetButtonClicked(object sender, EventArgs e)
    {
        _scopesTextBox.Text = string.Join(" ", DefaultSettings.Scopes);
    }

    private async void OnTestAuthButtonClicked(object sender, EventArgs e)
    {
        var clientId = _clientIdTextBox.Text.Trim();
        var clientSecret = _clientSecretTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            _authStatusLabel.Text = "Введите Client ID";
            _authStatusLabel.ForeColor = Color.Red;
            return;
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            _authStatusLabel.Text = "Введите Client Secret";
            _authStatusLabel.ForeColor = Color.Red;
            return;
        }

        _testAuthButton.Enabled = false;
        _authStatusLabel.Text = "Тестирование...";
        _authStatusLabel.ForeColor = Color.Blue;

        TwitchOAuthService.StatusChanged += OnOAuthStatusChanged;

        try
        {
            var scopes = _scopesTextBox.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var redirectUri = _redirectUriTextBox.Text.Trim();

            if (scopes.Length == 0)
            {
                scopes = DefaultSettings.Scopes;
            }

            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                redirectUri = DefaultSettings.RedirectUri;
            }

            var accessToken = await TwitchOAuthService.StartOAuthFlowAsync(clientId, clientSecret, scopes, redirectUri);

            if (string.IsNullOrEmpty(accessToken) == false)
            {
                _authStatusLabel.Text = "Авторизация успешна!";
                _authStatusLabel.ForeColor = Color.Green;

                LoadTokenInformation();
                _hasChanges = true;
                UpdateButtonStates();
            }
        }
        catch (Exception exception)
        {
            _authStatusLabel.Text = $"Ошибка: {exception.Message}";
            _authStatusLabel.ForeColor = Color.Red;
        }
        finally
        {
            TwitchOAuthService.StatusChanged -= OnOAuthStatusChanged;
            _testAuthButton.Enabled = true;
        }
    }

    private void OnOAuthStatusChanged(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnOAuthStatusChanged), message);
            return;
        }

        _authStatusLabel.Text = message;
        _authStatusLabel.ForeColor = Color.Blue;
    }

    private async void OnValidateTokenButtonClicked(object sender, EventArgs e)
    {
        var accessToken = _settings.Twitch.AccessToken;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _tokenStatusValueLabel.Text = "Токен отсутствует";
            _tokenStatusValueLabel.ForeColor = Color.Red;
            return;
        }

        _validateTokenButton.Enabled = false;
        _tokenStatusValueLabel.Text = "Проверка...";
        _tokenStatusValueLabel.ForeColor = Color.Blue;

        try
        {
            var isValid = await TwitchOAuthService.IsTokenValidAsync(accessToken);

            if (isValid)
            {
                _tokenStatusValueLabel.Text = "Действителен";
                _tokenStatusValueLabel.ForeColor = Color.Green;
            }
            else
            {
                _tokenStatusValueLabel.Text = "Недействителен";
                _tokenStatusValueLabel.ForeColor = Color.Red;
            }
        }
        catch (Exception exception)
        {
            _tokenStatusValueLabel.Text = $"Ошибка: {exception.Message}";
            _tokenStatusValueLabel.ForeColor = Color.Red;
        }
        finally
        {
            _validateTokenButton.Enabled = true;
        }
    }

    private async void OnRefreshTokenButtonClicked(object sender, EventArgs e)
    {
        var clientId = _settings.Twitch.ClientId;
        var clientSecret = _settings.Twitch.ClientSecret;
        var refreshToken = _settings.Twitch.RefreshToken;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            MessageBox.Show("Client ID и Client Secret должны быть настроены для обновления токена.",
                "Настройки отсутствуют", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            MessageBox.Show("Refresh Token отсутствует. Требуется повторная авторизация.",
                "Refresh Token отсутствует", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        _refreshTokenButton.Enabled = false;
        _tokenStatusValueLabel.Text = "Обновление...";
        _tokenStatusValueLabel.ForeColor = Color.Blue;

        TwitchOAuthService.StatusChanged += OnTokenOperationStatusChanged;

        try
        {
            var tokenResponse = await TwitchOAuthService.RefreshTokenAsync(clientId, clientSecret, refreshToken);

            _settings.Twitch.AccessToken = tokenResponse.AccessToken;
            _settings.Twitch.RefreshToken = tokenResponse.RefreshToken;

            SettingsManager.SaveSettings(_settings);

            LoadTokenInformation();
            _tokenStatusValueLabel.Text = "Обновлен успешно";
            _tokenStatusValueLabel.ForeColor = Color.Green;
            _lastRefreshValueLabel.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            _lastRefreshValueLabel.ForeColor = Color.Black;

            _hasChanges = true;
            UpdateButtonStates();
        }
        catch (Exception exception)
        {
            _tokenStatusValueLabel.Text = $"Ошибка: {exception.Message}";
            _tokenStatusValueLabel.ForeColor = Color.Red;

            MessageBox.Show($"Не удалось обновить токен: {exception.Message}",
                "Ошибка обновления токена", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            TwitchOAuthService.StatusChanged -= OnTokenOperationStatusChanged;
            _refreshTokenButton.Enabled = true;
        }
    }

    private void OnClearTokensButtonClicked(object sender, EventArgs e)
    {
        var result = MessageBox.Show("Вы уверены, что хотите очистить сохраненные токены?\n\nЭто потребует повторной авторизации при следующем подключении.",
            "Подтверждение очистки токенов",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _settings.Twitch.AccessToken = string.Empty;
        _settings.Twitch.RefreshToken = string.Empty;

        SettingsManager.SaveSettings(_settings);

        LoadTokenInformation();
        _tokenStatusValueLabel.Text = "Токены очищены";
        _tokenStatusValueLabel.ForeColor = Color.Orange;
        _lastRefreshValueLabel.Text = "Неизвестно";
        _lastRefreshValueLabel.ForeColor = Color.Gray;

        _hasChanges = true;
        UpdateButtonStates();
    }

    private void OnTokenOperationStatusChanged(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnTokenOperationStatusChanged), message);
            return;
        }

        _tokenStatusValueLabel.Text = message;
        _tokenStatusValueLabel.ForeColor = Color.Blue;
    }

    private void OnShowTokenButtonClicked(object sender, EventArgs e)
    {
        _tokensVisible = !_tokensVisible;
        UpdateTokenDisplay();
        UpdateShowTokenButton();
    }

    private static void CopySettings(AppSettings source, AppSettings destination)
    {
        destination.Twitch.BotUsername = source.Twitch.BotUsername;
        destination.Twitch.Channel = source.Twitch.Channel;
        destination.Twitch.MessagesAllowedInPeriod = source.Twitch.MessagesAllowedInPeriod;
        destination.Twitch.ThrottlingPeriodSeconds = source.Twitch.ThrottlingPeriodSeconds;
        destination.Twitch.ClientId = source.Twitch.ClientId;
        destination.Twitch.ClientSecret = source.Twitch.ClientSecret;
        destination.Twitch.AccessToken = source.Twitch.AccessToken;
        destination.Twitch.RefreshToken = source.Twitch.RefreshToken;
        destination.Twitch.RedirectUri = source.Twitch.RedirectUri;
        destination.Twitch.Scopes = source.Twitch.Scopes;
        destination.Ui.ShowLogsPanel = source.Ui.ShowLogsPanel;
        destination.Ui.ShowChatPanel = source.Ui.ShowChatPanel;
    }

    private void UpdateShowTokenButton()
    {
        _showTokenButton.Text = _tokensVisible ? "🙈" : "👁";
    }

    private void UpdateTokenDisplay()
    {
        var accessToken = _settings.Twitch.AccessToken;
        var refreshToken = _settings.Twitch.RefreshToken;

        _accessTokenTextBox.Text = string.IsNullOrWhiteSpace(accessToken) == false ? accessToken : "Не установлен";
        _refreshTokenTextBox.Text = string.IsNullOrWhiteSpace(refreshToken) == false ? refreshToken : "Не установлен";

        _accessTokenTextBox.UseSystemPasswordChar = _tokensVisible == false && string.IsNullOrWhiteSpace(accessToken) == false;
        _refreshTokenTextBox.UseSystemPasswordChar = _tokensVisible == false && string.IsNullOrWhiteSpace(refreshToken) == false;
    }

    private void SetPlaceholders()
    {
        _botUsernameTextBox.PlaceholderText = DefaultSettings.BotUsername;
        _channelTextBox.PlaceholderText = DefaultSettings.Channel;
        _clientIdTextBox.PlaceholderText = string.IsNullOrWhiteSpace(DefaultSettings.ClientId) ? "Введите Client ID" : DefaultSettings.ClientId;
        _clientSecretTextBox.PlaceholderText = string.IsNullOrWhiteSpace(DefaultSettings.ClientSecret) ? "Введите Client Secret" : DefaultSettings.ClientSecret;
        _redirectUriTextBox.PlaceholderText = DefaultSettings.RedirectUri;
        _scopesTextBox.PlaceholderText = string.Join(" ", DefaultSettings.Scopes);
    }

    private void LoadSettingsToControls()
    {
        _botUsernameTextBox.Text = _settings.Twitch.BotUsername;
        _channelTextBox.Text = _settings.Twitch.Channel;
        _messagesAllowedNumeric.Value = _settings.Twitch.MessagesAllowedInPeriod;
        _throttlingPeriodNumeric.Value = _settings.Twitch.ThrottlingPeriodSeconds;

        _clientIdTextBox.Text = _settings.Twitch.ClientId;
        _clientSecretTextBox.Text = _settings.Twitch.ClientSecret;
        _redirectUriTextBox.Text = _settings.Twitch.RedirectUri;
        _scopesTextBox.Text = string.Join(" ", _settings.Twitch.Scopes);

        LoadTokenInformation();

        _hasChanges = false;
        UpdateButtonStates();
    }

    private void SaveSettingsFromControls()
    {
        _settings.Twitch.BotUsername = _botUsernameTextBox.Text.Trim();
        _settings.Twitch.Channel = _channelTextBox.Text.Trim();
        _settings.Twitch.MessagesAllowedInPeriod = (int)_messagesAllowedNumeric.Value;
        _settings.Twitch.ThrottlingPeriodSeconds = (int)_throttlingPeriodNumeric.Value;

        _settings.Twitch.ClientId = _clientIdTextBox.Text.Trim();
        _settings.Twitch.ClientSecret = _clientSecretTextBox.Text.Trim();
        _settings.Twitch.RedirectUri = _redirectUriTextBox.Text.Trim();
        _settings.Twitch.Scopes = _scopesTextBox.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private void UpdateButtonStates()
    {
        _applyButton.Enabled = _hasChanges;
    }

    private void LoadTokenInformation()
    {
        var accessToken = _settings.Twitch.AccessToken;
        var refreshToken = _settings.Twitch.RefreshToken;

        UpdateTokenDisplay();
        UpdateShowTokenButton();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _tokenStatusValueLabel.Text = "Токен отсутствует";
            _tokenStatusValueLabel.ForeColor = Color.Red;
        }
        else
        {
            _tokenStatusValueLabel.Text = "Не проверен";
            _tokenStatusValueLabel.ForeColor = Color.Gray;
        }

        var hasTokens = string.IsNullOrWhiteSpace(accessToken) == false;
        var hasRefreshToken = string.IsNullOrWhiteSpace(refreshToken) == false;
        var hasCredentials = string.IsNullOrWhiteSpace(_settings.Twitch.ClientId) == false && string.IsNullOrWhiteSpace(_settings.Twitch.ClientSecret) == false;

        _validateTokenButton.Enabled = hasTokens;
        _refreshTokenButton.Enabled = hasRefreshToken && hasCredentials;
        _clearTokensButton.Enabled = hasTokens || hasRefreshToken;
        _showTokenButton.Enabled = hasTokens || hasRefreshToken;
    }

    private void ApplySettings()
    {
        try
        {
            SaveSettingsFromControls();
            SettingsManager.SaveSettings(_settings);
            _hasChanges = false;
            UpdateButtonStates();

            MessageBox.Show("Настройки успешно сохранены.", "Настройки",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Ошибка сохранения настроек: {exception.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
