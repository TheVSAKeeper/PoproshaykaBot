using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class BroadcasterIdProvider(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
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

            var user = await helix.GetUserByLoginAsync(channel, cancellationToken);

            if (user == null)
            {
                logger.LogWarning("Broadcaster id не получен для канала {Channel}", channel);
                return null;
            }

            _cachedChannel = channel;
            _cachedId = user.Id;
            return _cachedId;
        }
        finally
        {
            _lock.Release();
        }
    }
}
