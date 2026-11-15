using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class SettingsSessionState
{
    private string _twitchChannel = string.Empty;

    public required AppSettings Settings { get; init; }

    public string TwitchChannel => _twitchChannel;

    public event Action<string>? TwitchChannelChanged;

    public void UpdateTwitchChannel(string? channel)
    {
        var normalized = channel ?? string.Empty;
        if (string.Equals(_twitchChannel, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _twitchChannel = normalized;
        TwitchChannelChanged?.Invoke(normalized);
    }
}
