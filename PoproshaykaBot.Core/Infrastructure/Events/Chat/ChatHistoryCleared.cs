using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatHistoryCleared(StreamingPlatform Platform, string? Channel = null) : EventBase, IPlatformEvent;
