using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Twitch.Chat;

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

    [Inject]
    public IBotUserIdProvider BotUserIdProvider { get; internal init; } = null!;

    [Inject]
    public ILogger<BasicSettingsControl> Logger { get; internal init; } = null!;

    public void LoadSettings(TwitchSettings settings)
    {
        _botUsernameTextBox.Text = settings.BotAccount.Login;
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

        SetPlaceholders();
        _ = RefreshBotLoginFromTokenAsync();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChannelResetButtonClicked(object sender, EventArgs e)
    {
        ResetChannel();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetChannel()
    {
        _channelTextBox.Text = DefaultSettings.Channel;
    }

    private void SetPlaceholders()
    {
        _botUsernameTextBox.PlaceholderText = "Авторизуйте бота на вкладке OAuth";
        _channelTextBox.PlaceholderText = DefaultSettings.Channel;
    }

    private async Task RefreshBotLoginFromTokenAsync()
    {
        try
        {
            var user = await BotUserIdProvider.GetUserAsync(CancellationToken.None);

            if (user == null || string.IsNullOrEmpty(user.Login))
            {
                return;
            }

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(() =>
            {
                _botUsernameTextBox.Text = user.Login;
            });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Не удалось получить имя бота из токена (возможно, бот ещё не авторизован)");
        }
    }
}
