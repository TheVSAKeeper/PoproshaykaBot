using PoproshaykaBot.WinForms.Broadcast;

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
        _streamEndStatsMessageTextBox.Text = settings.StreamEndStatsMessage;
        _broadcastIntervalNumericUpDown.Value = Math.Max(1, settings.BroadcastIntervalMinutes);
        _broadcastTemplateTextBox.Text = settings.BroadcastMessageTemplate;
    }

    public void SaveSettings(AutoBroadcastSettings settings)
    {
        settings.AutoBroadcastEnabled = _autoBroadcastEnabledCheckBox.Checked;
        settings.StreamStatusNotificationsEnabled = _streamNotificationsEnabledCheckBox.Checked;
        settings.StreamStartMessage = _streamStartMessageTextBox.Text.Trim();
        settings.StreamStopMessage = _streamStopMessageTextBox.Text.Trim();
        settings.StreamEndStatsMessage = _streamEndStatsMessageTextBox.Text.Trim();
        settings.BroadcastIntervalMinutes = (int)_broadcastIntervalNumericUpDown.Value;
        settings.BroadcastMessageTemplate = _broadcastTemplateTextBox.Text.Trim();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPlaceholders()
    {
        _streamStartMessageTextBox.PlaceholderText = DefaultSettings.StreamStartMessage;
        _streamStopMessageTextBox.PlaceholderText = DefaultSettings.StreamStopMessage;
        _streamEndStatsMessageTextBox.PlaceholderText = DefaultSettings.StreamEndStatsMessage;
        _broadcastTemplateTextBox.PlaceholderText = DefaultSettings.BroadcastMessageTemplate;
    }
}
