namespace PoproshaykaBot.WinForms;

public partial class SettingsForm : Form
{
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
}
