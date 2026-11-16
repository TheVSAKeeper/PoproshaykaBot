using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record PlatformChatAvailabilityChanged(
    StreamingPlatform Platform,
    bool IsAvailable)
    : EventBase, IPlatformEvent;
