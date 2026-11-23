using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record RawChatHistoryReceived(
    ChatHistoryScope Scope,
    IReadOnlyList<(ChatMessage Message, DateTimeOffset Timestamp)> Messages,
    IReadOnlyList<ChatModerationAction>? Moderations = null,
    bool AllowExternalHistory = false) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => Scope.Platform;
}
