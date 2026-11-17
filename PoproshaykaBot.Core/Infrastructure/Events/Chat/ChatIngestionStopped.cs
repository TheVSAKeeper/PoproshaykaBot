using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatIngestionStopped(
    StreamingPlatform Platform,
    DateTimeOffset At,
    string? ChannelId = null) : EventBase, IPlatformEvent;
