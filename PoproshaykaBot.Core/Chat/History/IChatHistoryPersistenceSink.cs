namespace PoproshaykaBot.Core.Chat.History;

public interface IChatHistoryPersistenceSink
{
    void MarkDirty(ChatHistoryScope scope);

    ChatHistoryClearResult ClearScopes(IReadOnlyList<ChatHistoryClearTarget> targets, bool clearUnloadedScopes = false);
}

public sealed record ChatHistoryPersistenceSnapshot(
    ChatHistoryScope Scope,
    ulong ClearGeneration,
    ulong MutationVersion,
    bool HasAnyState,
    ChatHistoryCacheDocument Document);

public sealed record ChatHistoryClearTarget(
    ChatHistoryScope Scope,
    ulong ClearGeneration,
    ulong MutationVersion,
    ChatHistoryCacheDocument Document,
    bool HadStateBeforeClear = true,
    Func<ChatHistoryPersistenceSnapshot?>? RefreshSnapshot = null);
