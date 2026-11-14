using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamWentOffline(StreamingPlatform Platform, string Channel, bool IsCatchUp = false) : EventBase, IPlatformEvent;
