using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Forms.Settings;

public partial class BasicSettingsControl : UserControl
{
    private static readonly TwitchSettings DefaultSettings = new();

    private static readonly (TwitchOAuthRole Role, string Display)[] ChatAccountOptions =
    [
        (TwitchOAuthRole.Bot, "бота"),
        (TwitchOAuthRole.Broadcaster, "стримера"),
    ];

    private bool _initialized;

    public BasicSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(TwitchSettings settings)
    {
        _channelTextBox.Text = settings.Channel;

        if (_chatAccountComboBox.Items.Count == 0)
        {
            foreach (var option in ChatAccountOptions)
            {
                _chatAccountComboBox.Items.Add(option.Display);
            }
        }

        var index = Array.FindIndex(ChatAccountOptions, o => o.Role == settings.ChatDisplayAccount);
        _chatAccountComboBox.SelectedIndex = index >= 0 ? index : 0;
    }

    public void SaveSettings(TwitchSettings settings)
    {
        settings.Channel = _channelTextBox.Text.Trim();

        if (_chatAccountComboBox.SelectedIndex >= 0
            && _chatAccountComboBox.SelectedIndex < ChatAccountOptions.Length)
        {
            settings.ChatDisplayAccount = ChatAccountOptions[_chatAccountComboBox.SelectedIndex].Role;
        }
    }

    public string GetChannel()
    {
        return _channelTextBox.Text.Trim();
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
