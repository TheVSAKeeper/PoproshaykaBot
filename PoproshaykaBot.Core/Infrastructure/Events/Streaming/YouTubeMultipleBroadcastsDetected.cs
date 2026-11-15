using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record YouTubeMultipleBroadcastsDetected(
    string ChannelId,
    IReadOnlyList<YouTubeBroadcastSummary> Candidates)
    : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;
}
