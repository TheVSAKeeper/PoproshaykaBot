namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal enum YouTubeLiveChatItemKind
{
    None = 0,
    TextMessage = 1,
    SuperChat = 2,
    SuperSticker = 3,
    FanFunding = 4,
    NewSponsor = 5,
    MemberMilestoneChat = 6,
    MembershipGifting = 7,
    GiftMembershipReceived = 8,
    Gift = 9,
    ChatEnded = 10,
    Tombstone = 11,
    UserBanned = 12,
    Poll = 13,
    Other = 14,
}

internal sealed record YouTubeLiveChatAuthor(
    string ChannelId,
    string DisplayName,
    bool IsChatOwner,
    bool IsChatModerator,
    bool IsChatSponsor,
    bool IsVerified);

internal sealed record YouTubeLiveChatItem(
    string Id,
    YouTubeLiveChatItemKind Kind,
    DateTimeOffset? PublishedAtUtc,
    string MessageText,
    string DisplayMessage,
    YouTubeLiveChatAuthor Author,
    string RawEventType);

internal sealed record YouTubeLiveChatBatch(
    IReadOnlyList<YouTubeLiveChatItem> Items,
    string? NextPageToken,
    TimeSpan PollingInterval,
    DateTimeOffset? OfflineAtUtc);

internal interface IYouTubeLiveChatStreamClient
{
    IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken);
}

internal interface IYouTubeLiveChatPollingClient
{
    Task<YouTubeLiveChatBatch> ListMessagesAsync(
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken);
}
