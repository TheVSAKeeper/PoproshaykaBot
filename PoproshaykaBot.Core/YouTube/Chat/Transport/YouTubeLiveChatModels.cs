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

internal enum YouTubeChatMode
{
    None = 0,
    LiveChat = 1,
    TopChat = 2,
}

internal enum YouTubeChatTransportKind
{
    None = 0,
    InnerTube = 1,
    OfficialGrpc = 2,
    OfficialRest = 3,
}

internal enum YouTubeChatRunKind
{
    None = 0,
    Text = 1,
    Emoji = 2,
}

internal enum YouTubeMembershipEventKind
{
    None = 0,
    NewMember = 1,
    Upgraded = 2,
    Milestone = 3,
    GiftPurchase = 4,
    GiftRedemption = 5,
}

internal enum YouTubeModerationActionKind
{
    None = 0,
    MessageDeleted = 1,
    AuthorMessagesDeleted = 2,
    UserBannedTemporary = 3,
    UserBannedPermanent = 4,
    MessageReplaced = 5,
}

internal sealed record YouTubeChatReaderOptions
{
    public bool RetainRawPayloads { get; init; }
}

internal sealed record YouTubeChatTarget(
    string? VideoId,
    string? LiveChatId,
    string? ChannelId = null,
    YouTubeChatMode Mode = YouTubeChatMode.LiveChat,
    bool RequiresOfficialMessageIds = false);

internal sealed record YouTubeChatCursorToken(
    YouTubeChatTransportKind Transport,
    YouTubeChatMode Mode,
    string Value);

internal sealed record YouTubeChatRun(
    YouTubeChatRunKind Kind,
    string Text,
    string? EmojiId = null,
    string? Shortcut = null,
    string? ImageUrl = null,
    bool IsCustomEmoji = false);

internal sealed record YouTubeMonetaryDetails(
    long? AmountMicros,
    decimal? AmountValue,
    string? Currency,
    string AmountDisplayString,
    uint? Tier = null,
    string? HeaderBackgroundColorHex = null,
    string? BodyBackgroundColorHex = null,
    string? StickerId = null,
    string? StickerAltText = null,
    string? StickerImageUrl = null,
    int? JewelsAmount = null);

internal sealed record YouTubeMembershipDetails(
    YouTubeMembershipEventKind EventKind,
    string? LevelName = null,
    int? MilestoneMonths = null,
    int? GiftCount = null,
    string? GifterChannelId = null,
    string? GifterDisplayName = null,
    string? AssociatedGiftingMessageId = null,
    string? HeaderText = null);

internal sealed record YouTubeModerationDetails(
    YouTubeModerationActionKind ActionKind,
    string? TargetMessageId = null,
    string? TargetChannelId = null,
    TimeSpan? BanDuration = null,
    YouTubeLiveChatItem? ReplacementItem = null);

internal sealed record YouTubeLiveChatAuthor(
    string ChannelId,
    string DisplayName,
    bool IsChatOwner,
    bool IsChatModerator,
    bool IsChatSponsor,
    bool IsVerified,
    string? AvatarUrl = null,
    string? Handle = null,
    string? MembershipBadgeLabel = null);

internal sealed record YouTubeLiveChatItem(
    string Id,
    YouTubeLiveChatItemKind Kind,
    DateTimeOffset? PublishedAtUtc,
    string MessageText,
    string DisplayMessage,
    YouTubeLiveChatAuthor Author,
    string RawEventType,
    IReadOnlyList<YouTubeChatRun>? Runs = null,
    YouTubeMonetaryDetails? Monetary = null,
    YouTubeMembershipDetails? Membership = null,
    YouTubeModerationDetails? Moderation = null,
    string? RawPayloadJson = null);

internal sealed record YouTubeLiveChatBatch(
    IReadOnlyList<YouTubeLiveChatItem> Items,
    string? NextPageToken,
    TimeSpan PollingInterval,
    DateTimeOffset? OfflineAtUtc,
    YouTubeChatCursorToken? NextCursor = null,
    YouTubeChatTransportKind Transport = YouTubeChatTransportKind.None);

internal interface IYouTubeLiveChatReader
{
    YouTubeChatTransportKind TransportKind { get; }

    bool IsServerStream { get; }

    bool CanRead(YouTubeChatTarget target);

    IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        CancellationToken cancellationToken);

    IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        CancellationToken cancellationToken);
}

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
