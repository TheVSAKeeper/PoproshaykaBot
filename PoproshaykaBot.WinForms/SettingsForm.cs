namespace PoproshaykaBot.WinForms;

public class SettingsForm : Form
{
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    private Button _applyButton = null!;
    private Button _resetButton = null!;

    private TextBox _botUsernameTextBox = null!;
    private TextBox _channelTextBox = null!;
    private NumericUpDown _messagesAllowedNumeric = null!;
    private NumericUpDown _throttlingPeriodNumeric = null!;

    private TextBox _clientIdTextBox = null!;
    private TextBox _clientSecretTextBox = null!;
    private TextBox _redirectUriTextBox = null!;
    private TextBox _scopesTextBox = null!;

    private AppSettings _settings;
    private bool _hasChanges;

    public SettingsForm()
    {
        _settings = new();
        CopySettings(SettingsManager.Current, _settings);
        InitializeComponent();
        LoadSettingsToControls();
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


    private void InitializeComponent()
    {
        _okButton = new();
        _cancelButton = new();
        _applyButton = new();
        _resetButton = new();

        SuspendLayout();

        InitializeTwitchControls();

        _okButton.Location = new(335, 410);
        _okButton.Size = new(75, 23);
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Click += OnOkButtonClicked;

        _cancelButton.Location = new(416, 410);
        _cancelButton.Size = new(75, 23);
        _cancelButton.Text = "Отмена";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Click += OnCancelButtonClicked;

        _applyButton.Location = new(497, 410);
        _applyButton.Size = new(75, 23);
        _applyButton.Text = "Применить";
        _applyButton.Enabled = false;
        _applyButton.Click += OnApplyButtonClicked;

        _resetButton.Location = new(12, 410);
        _resetButton.Size = new(75, 23);
        _resetButton.Text = "Сброс";
        _resetButton.Click += OnResetButtonClicked;

        ClientSize = new(584, 445);
        Text = "Настройки Twitch бота";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(_okButton);
        Controls.Add(_cancelButton);
        Controls.Add(_applyButton);
        Controls.Add(_resetButton);

        ResumeLayout(false);
    }


    private void InitializeTwitchControls()
    {
        var botUsernameLabel = new Label { Text = "Имя пользователя бота:", Location = new(15, 20), Size = new(150, 15) };

        _botUsernameTextBox = new()
            { Location = new(170, 17), Size = new(200, 23) };

        _botUsernameTextBox.TextChanged += OnSettingChanged;

        var channelLabel = new Label { Text = "Канал:", Location = new(15, 50), Size = new(150, 15) };

        _channelTextBox = new()
            { Location = new(170, 47), Size = new(200, 23) };

        _channelTextBox.TextChanged += OnSettingChanged;

        var messagesAllowedLabel = new Label { Text = "Сообщений в период:", Location = new(15, 80), Size = new(150, 15) };

        _messagesAllowedNumeric = new()
            { Location = new(170, 77), Size = new(100, 23), Minimum = 1, Maximum = 1000, Value = 750 };

        _messagesAllowedNumeric.ValueChanged += OnSettingChanged;

        var throttlingPeriodLabel = new Label { Text = "Период ограничения (сек):", Location = new(15, 110), Size = new(150, 15) };

        _throttlingPeriodNumeric = new()
            { Location = new(170, 107), Size = new(100, 23), Minimum = 1, Maximum = 300, Value = 30 };

        _throttlingPeriodNumeric.ValueChanged += OnSettingChanged;

        var oauthGroupBox = new GroupBox { Text = "OAuth настройки", Location = new(15, 140), Size = new(355, 180) };

        var clientIdLabel = new Label { Text = "Client ID:", Location = new(10, 25), Size = new(80, 15) };

        _clientIdTextBox = new()
            { Location = new(95, 22), Size = new(250, 23) };

        _clientIdTextBox.TextChanged += OnSettingChanged;

        var clientSecretLabel = new Label { Text = "Client Secret:", Location = new(10, 55), Size = new(80, 15) };

        _clientSecretTextBox = new()
            { Location = new(95, 52), Size = new(250, 23), UseSystemPasswordChar = true };

        _clientSecretTextBox.TextChanged += OnSettingChanged;

        var redirectUriLabel = new Label { Text = "Redirect URI:", Location = new(10, 85), Size = new(80, 15) };

        _redirectUriTextBox = new()
            { Location = new(95, 82), Size = new(250, 23) };

        _redirectUriTextBox.TextChanged += OnSettingChanged;

        var scopesLabel = new Label { Text = "Scopes:", Location = new(10, 115), Size = new(80, 15) };

        _scopesTextBox = new()
            { Location = new(95, 112), Size = new(250, 23) };

        _scopesTextBox.TextChanged += OnSettingChanged;

        var oauthInfoLabel = new Label
        {
            Text = "Получите Client ID и Client Secret на https://dev.twitch.tv/console/apps\nScopes разделяйте пробелами (например: chat:read chat:edit)",
            Location = new(10, 145),
            Size = new(335, 30),
            ForeColor = Color.Gray,
        };

        oauthGroupBox.Controls.AddRange(new Control[]
        {
            clientIdLabel, _clientIdTextBox,
            clientSecretLabel, _clientSecretTextBox,
            redirectUriLabel, _redirectUriTextBox,
            scopesLabel, _scopesTextBox,
            oauthInfoLabel,
        });

        Controls.AddRange(new Control[]
        {
            botUsernameLabel, _botUsernameTextBox,
            channelLabel, _channelTextBox,
            messagesAllowedLabel, _messagesAllowedNumeric,
            throttlingPeriodLabel, _throttlingPeriodNumeric,
            oauthGroupBox,
        });
    }
}
