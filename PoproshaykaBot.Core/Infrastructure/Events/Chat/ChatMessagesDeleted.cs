using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatMessagesDeleted(
    ChatHistoryScope Scope,
    IReadOnlyList<string> DeletedMessageIds,
    string? ModeratedUserId = null,
    DateTimeOffset? CutoffUtc = null,
    bool AllowReplacement = false) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => Scope.Platform;
}
