using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Twitch.Chat;

namespace PoproshaykaBot.WinForms.Settings;

public partial class BasicSettingsControl : UserControl
{
    private static readonly TwitchSettings DefaultSettings = new();

    private IBotUserIdProvider? _botUserIdProvider;
    private ILogger<BasicSettingsControl>? _logger;

    public BasicSettingsControl()
    {
        InitializeComponent();
        SetPlaceholders();
    }

    public event EventHandler? SettingChanged;

    public void Setup(IBotUserIdProvider botUserIdProvider, ILogger<BasicSettingsControl> logger)
    {
        _botUserIdProvider = botUserIdProvider;
        _logger = logger;

        _ = RefreshBotLoginFromTokenAsync();
    }

    public void LoadSettings(TwitchSettings settings)
    {
        _botUsernameTextBox.Text = settings.BotUsername;
        _channelTextBox.Text = settings.Channel;
    }

    public void SaveSettings(TwitchSettings settings)
    {
        settings.BotUsername = _botUsernameTextBox.Text.Trim();
        settings.Channel = _channelTextBox.Text.Trim();
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
        _botUsernameTextBox.PlaceholderText = DefaultSettings.BotUsername;
        _channelTextBox.PlaceholderText = DefaultSettings.Channel;
    }

    private async Task RefreshBotLoginFromTokenAsync()
    {
        if (_botUserIdProvider == null)
        {
            return;
        }

        try
        {
            var user = await _botUserIdProvider.GetUserAsync(CancellationToken.None);

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
            _logger?.LogDebug(ex, "Не удалось получить имя бота из токена (возможно, бот ещё не авторизован)");
        }
    }
}
