using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings;

public partial class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private AppSettings _settings;
    private bool _initialized;
    private bool _hasChanges;

    public SettingsForm(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _settings = CopySettings(settingsManager.Current);

        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

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

    private static AppSettings CopySettings(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    private static void CopyLiveTokens(TwitchAccountSettings source, TwitchAccountSettings target)
    {
        target.AccessToken = source.AccessToken;
        target.RefreshToken = source.RefreshToken;
        target.Login = source.Login;
        target.UserId = source.UserId;
        target.StoredScopes = source.StoredScopes;
    }

    private void LoadSettingsToControls()
    {
        _basicSettingsControl.LoadSettings(_settings.Twitch);
        _rateLimitingSettingsControl.LoadSettings(_settings.Twitch);
        _messagesSettingsControl.LoadSettings(_settings.Twitch.Messages);
        _httpServerSettingsControl.LoadSettings(_settings.Twitch);
        _oauthSettingsControl.LoadSettings(_settings);
        _obsChatSettingsControl.LoadSettings(_settings.Twitch.ObsChat);
        _autoBroadcastSettingsControl.LoadSettings(_settings.Twitch.AutoBroadcast);
        _miscSettingsControl.LoadSettings(_settings);
        _pollsSettingsControl.LoadSettings(_settings.Twitch.Polls);
        _dashboardSettingsControl.LoadSettings(_settings.Ui);

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
        _obsChatSettingsControl.SaveSettings(_settings.Twitch.ObsChat);
        _autoBroadcastSettingsControl.SaveSettings(_settings.Twitch.AutoBroadcast);
        _miscSettingsControl.SaveSettings(_settings);
        _pollsSettingsControl.SaveSettings(_settings.Twitch.Polls);
        _dashboardSettingsControl.SaveSettings(_settings.Ui);
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
            var live = _settingsManager.Current;
            CopyLiveTokens(live.Twitch.BotAccount, _settings.Twitch.BotAccount);
            CopyLiveTokens(live.Twitch.BroadcasterAccount, _settings.Twitch.BroadcasterAccount);
            _settings.Twitch.BroadcastProfiles = live.Twitch.BroadcastProfiles;
            _settings.Twitch.Polls.Profiles = live.Twitch.Polls.Profiles;
            _settings.Twitch.Infrastructure.RecentCategories = live.Twitch.Infrastructure.RecentCategories;
            _settingsManager.SaveSettings(_settings);
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
