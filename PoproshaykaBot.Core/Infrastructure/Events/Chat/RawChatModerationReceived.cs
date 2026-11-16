using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record RawChatModerationReceived(
    ChatHistoryScope Scope,
    ChatModerationAction Action) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => Scope.Platform;
}
