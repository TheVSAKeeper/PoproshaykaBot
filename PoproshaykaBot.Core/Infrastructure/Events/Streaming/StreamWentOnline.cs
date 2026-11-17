using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamWentOnline(
    StreamingPlatform Platform,
    string Channel,
    StreamInfo? Stream,
    bool IsCatchUp = false,
    string? ChannelId = null) : EventBase, IPlatformEvent;
