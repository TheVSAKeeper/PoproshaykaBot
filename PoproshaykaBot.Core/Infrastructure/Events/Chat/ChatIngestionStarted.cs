using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatIngestionStarted(StreamingPlatform Platform, DateTimeOffset At) : EventBase, IPlatformEvent;
