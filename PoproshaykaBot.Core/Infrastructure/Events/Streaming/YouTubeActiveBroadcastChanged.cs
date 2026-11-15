using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record YouTubeActiveBroadcastChanged(YouTubeBroadcastSummary? ActiveBroadcast)
    : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;
}
