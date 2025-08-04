namespace PoproshaykaBot.WinForms.Settings;

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
        destination.Twitch.HttpServerPort = source.Twitch.HttpServerPort;
        destination.Twitch.HttpServerEnabled = source.Twitch.HttpServerEnabled;
        destination.Twitch.ObsOverlayEnabled = source.Twitch.ObsOverlayEnabled;
        destination.Twitch.Messages.WelcomeEnabled = source.Twitch.Messages.WelcomeEnabled;
        destination.Twitch.Messages.Welcome = source.Twitch.Messages.Welcome;
        destination.Twitch.Messages.FarewellEnabled = source.Twitch.Messages.FarewellEnabled;
        destination.Twitch.Messages.Farewell = source.Twitch.Messages.Farewell;
        destination.Twitch.Messages.ConnectionEnabled = source.Twitch.Messages.ConnectionEnabled;
        destination.Twitch.Messages.Connection = source.Twitch.Messages.Connection;
        destination.Twitch.Messages.DisconnectionEnabled = source.Twitch.Messages.DisconnectionEnabled;
        destination.Twitch.Messages.Disconnection = source.Twitch.Messages.Disconnection;
        destination.Ui.ShowLogsPanel = source.Ui.ShowLogsPanel;
        destination.Ui.ShowChatPanel = source.Ui.ShowChatPanel;
    }

    private void LoadSettingsToControls()
    {
        _basicSettingsControl.LoadSettings(_settings.Twitch);
        _rateLimitingSettingsControl.LoadSettings(_settings.Twitch);
        _messagesSettingsControl.LoadSettings(_settings.Twitch.Messages);
        _httpServerSettingsControl.LoadSettings(_settings.Twitch);
        _oauthSettingsControl.LoadSettings(_settings);

        _hasChanges = false;
        UpdateButtonStates();
    }

    private void SaveSettingsFromControls()
    {
        _basicSettingsControl.SaveSettings(_settings.Twitch);
        _rateLimitingSettingsControl.SaveSettings(_settings.Twitch);
        _messagesSettingsControl.SaveSettings(_settings.Twitch.Messages);
        _httpServerSettingsControl.SaveSettings(_settings.Twitch);
        _oauthSettingsControl.SaveSettings(_settings);
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
