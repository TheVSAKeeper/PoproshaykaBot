using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Settings;
using TwitchLib.Api;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class BroadcasterIdProvider(
    TwitchAPI twitchApi,
    SettingsManager settingsManager,
    ILogger<BroadcasterIdProvider> logger)
    : IBroadcasterIdProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedChannel;
    private string? _cachedId;

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var channel = settingsManager.Current.Twitch.Channel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (string.Equals(_cachedChannel, channel, StringComparison.OrdinalIgnoreCase) && _cachedId != null)
            {
                return _cachedId;
            }

            var users = await twitchApi.Helix.Users.GetUsersAsync(logins: [channel]);

            if (users?.Users == null || users.Users.Length == 0)
            {
                logger.LogWarning("Broadcaster id не получен для канала {Channel}", channel);
                return null;
            }

            _cachedChannel = channel;
            _cachedId = users.Users[0].Id;
            return _cachedId;
        }
        finally
        {
            _lock.Release();
        }
    }
}
