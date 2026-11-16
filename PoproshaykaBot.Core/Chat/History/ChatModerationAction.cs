namespace PoproshaykaBot.Core.Chat.History;

public enum ChatModerationActionKind
{
    None = 0,
    MessageDeleted = 1,
    AuthorMessagesDeleted = 2,
    MessageReplaced = 3,
}

public sealed record ChatModerationAction(
    ChatModerationActionKind Kind,
    string? TargetMessageId,
    string? TargetUserId,
    DateTimeOffset CutoffUtc,
    string? ReplacementMessageId = null);

public sealed record ChatHistoryClearResult(bool IsComplete);

public sealed record ChatHistoryImportOutcome(
    ChatHistoryScope Scope,
    IReadOnlyList<ChatMessageData> AddedEntries,
    IReadOnlyList<ChatMessageData> MergedHistory,
    IReadOnlyList<string>? RemovedMessageIds = null,
    IReadOnlyList<(string UserId, DateTimeOffset CutoffUtc)>? ModeratedAuthors = null);
