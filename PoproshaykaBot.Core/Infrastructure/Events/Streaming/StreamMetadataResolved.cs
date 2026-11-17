using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamMetadataResolved(
    StreamingPlatform Platform,
    string Channel,
    StreamInfo Stream,
    string? ChannelId = null) : EventBase, IPlatformEvent;
