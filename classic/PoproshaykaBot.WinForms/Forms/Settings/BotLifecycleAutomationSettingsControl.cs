using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.WinForms.Forms.Settings;

public partial class BotLifecycleAutomationSettingsControl : UserControl
{
    public BotLifecycleAutomationSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(BotLifecycleAutomationSettings settings)
    {
        _autoConnectCheckBox.Checked = settings.AutoConnectOnStreamOnline;
        _autoDisconnectCheckBox.Checked = settings.AutoDisconnectOnStreamOffline;
    }

    public void SaveSettings(BotLifecycleAutomationSettings settings)
    {
        settings.AutoConnectOnStreamOnline = _autoConnectCheckBox.Checked;
        settings.AutoDisconnectOnStreamOffline = _autoDisconnectCheckBox.Checked;
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}
