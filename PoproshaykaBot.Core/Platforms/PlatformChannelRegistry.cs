using System.Collections.Concurrent;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Platforms;

public sealed class PlatformChannelRegistry : IChannelProvider
{
    private readonly ConcurrentDictionary<StreamingPlatform, string?> _channels = new();

    public string? Channel => GetChannel(StreamingPlatform.Twitch);

    public string? GetChannel(StreamingPlatform platform)
    {
        return _channels.TryGetValue(platform, out var channel) ? channel : null;
    }

    public void SetChannel(StreamingPlatform platform, string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            _channels.TryRemove(platform, out _);
            return;
        }

        _channels[platform] = channel;
    }

    public void SetChannelIfUnset(StreamingPlatform platform, string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _channels.TryAdd(platform, channel);
    }

    public void Clear(StreamingPlatform platform)
    {
        _channels.TryRemove(platform, out _);
    }

    public void ClearAll()
    {
        _channels.Clear();
    }
}
