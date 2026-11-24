using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record PlatformMultipleBroadcastsChanged(
    StreamingPlatform Platform,
    string ChannelId,
    int ActiveBroadcastCount)
    : EventBase, IPlatformEvent;
