using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Settings;

public partial class BasicSettingsControl : UserControl
{
    private static readonly TwitchSettings DefaultSettings = new();
    private bool _initialized;

    public BasicSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(TwitchSettings settings)
    {
        _channelTextBox.Text = settings.Channel;
    }

    public void SaveSettings(TwitchSettings settings)
    {
        settings.Channel = _channelTextBox.Text.Trim();
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

        _channelTextBox.PlaceholderText = DefaultSettings.Channel;
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChannelResetButtonClicked(object sender, EventArgs e)
    {
        _channelTextBox.Text = DefaultSettings.Channel;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}
