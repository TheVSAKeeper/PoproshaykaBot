using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatHistoryLoaded(
    ChatHistoryScope Scope,
    IReadOnlyList<ChatMessageData> AddedEntries,
    IReadOnlyList<ChatMessageData> MergedHistory) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => Scope.Platform;
}
