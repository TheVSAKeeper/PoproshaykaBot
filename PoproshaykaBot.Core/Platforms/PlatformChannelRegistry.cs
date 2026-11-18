using System.Collections.Concurrent;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Platforms;

public sealed record ActiveChannelBinding(
    ChannelKey Key,
    string ChannelLabel,
    string BotContextId,
    string BroadcasterContextId,
    long Generation);

public sealed class PlatformChannelRegistry : IChannelProvider
{
    private readonly ConcurrentDictionary<StreamingPlatform, string?> _channels = new();
    private readonly ConcurrentDictionary<StreamingPlatform, ActiveChannelBinding> _bindings = new();

    public string? Channel => GetChannel(StreamingPlatform.Twitch);

    public string? GetChannel(StreamingPlatform platform)
    {
        return _channels.TryGetValue(platform, out var channel) ? channel : null;
    }

    public ActiveChannelBinding? GetBinding(StreamingPlatform platform)
    {
        return _bindings.TryGetValue(platform, out var binding) ? binding : null;
    }

    public void SetChannel(StreamingPlatform platform, string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            _channels.TryRemove(platform, out _);
            _bindings.TryRemove(platform, out _);
            return;
        }

        _channels[platform] = channel;
    }

    public void SetBinding(ActiveChannelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var normalized = binding with
        {
            BotContextId = AuthContextIds.Normalize(binding.BotContextId),
            BroadcasterContextId = AuthContextIds.Normalize(binding.BroadcasterContextId),
        };
        _bindings[normalized.Key.Platform] = normalized;
        _channels[normalized.Key.Platform] = normalized.ChannelLabel;
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
        _bindings.TryRemove(platform, out _);
    }

    public void ClearAll()
    {
        _channels.Clear();
        _bindings.Clear();
    }
}
