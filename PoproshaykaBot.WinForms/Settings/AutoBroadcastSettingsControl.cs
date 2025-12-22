namespace PoproshaykaBot.WinForms.Settings;

public partial class AutoBroadcastSettingsControl : UserControl
{
    private static readonly AutoBroadcastSettings DefaultSettings = new();

    public AutoBroadcastSettingsControl()
    {
        InitializeComponent();
        SetPlaceholders();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(AutoBroadcastSettings settings)
    {
        _autoBroadcastEnabledCheckBox.Checked = settings.AutoBroadcastEnabled;
        _streamNotificationsEnabledCheckBox.Checked = settings.StreamStatusNotificationsEnabled;
        _streamStartMessageTextBox.Text = settings.StreamStartMessage;
        _streamStopMessageTextBox.Text = settings.StreamStopMessage;
        _broadcastIntervalNumericUpDown.Value = Math.Max(1, settings.BroadcastIntervalMinutes);
        _broadcastTemplateTextBox.Text = settings.BroadcastMessageTemplate;

        UpdateControlsState();
    }

    public void SaveSettings(AutoBroadcastSettings settings)
    {
        settings.AutoBroadcastEnabled = _autoBroadcastEnabledCheckBox.Checked;
        settings.StreamStatusNotificationsEnabled = _streamNotificationsEnabledCheckBox.Checked;
        settings.StreamStartMessage = _streamStartMessageTextBox.Text.Trim();
        settings.StreamStopMessage = _streamStopMessageTextBox.Text.Trim();
        settings.BroadcastIntervalMinutes = (int)_broadcastIntervalNumericUpDown.Value;
        settings.BroadcastMessageTemplate = _broadcastTemplateTextBox.Text.Trim();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        UpdateControlsState();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateControlsState()
    {
        var autoBroadcastEnabled = _autoBroadcastEnabledCheckBox.Checked;

        _streamNotificationsEnabledCheckBox.Enabled = autoBroadcastEnabled;
        _streamStartMessageTextBox.Enabled = autoBroadcastEnabled && _streamNotificationsEnabledCheckBox.Checked;
        _streamStopMessageTextBox.Enabled = autoBroadcastEnabled && _streamNotificationsEnabledCheckBox.Checked;
        _streamStartMessageLabel.Enabled = autoBroadcastEnabled;
        _streamStopMessageLabel.Enabled = autoBroadcastEnabled;
        _broadcastIntervalLabel.Enabled = autoBroadcastEnabled;
        _broadcastIntervalNumericUpDown.Enabled = autoBroadcastEnabled;
        _broadcastTemplateLabel.Enabled = autoBroadcastEnabled;
        _broadcastTemplateTextBox.Enabled = autoBroadcastEnabled;
    }

    private void SetPlaceholders()
    {
        _streamStartMessageTextBox.PlaceholderText = DefaultSettings.StreamStartMessage;
        _streamStopMessageTextBox.PlaceholderText = DefaultSettings.StreamStopMessage;
        _broadcastTemplateTextBox.PlaceholderText = DefaultSettings.BroadcastMessageTemplate;
    }
}
