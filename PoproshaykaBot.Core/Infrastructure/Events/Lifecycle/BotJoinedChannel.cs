using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record BotJoinedChannel(StreamingPlatform Platform, string Channel) : EventBase, IPlatformEvent;
