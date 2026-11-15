using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatReadingGapDetected(
    StreamingPlatform Platform,
    string Channel,
    string LiveChatId,
    DateTimeOffset DetectedAtUtc)
    : EventBase, IPlatformEvent;
