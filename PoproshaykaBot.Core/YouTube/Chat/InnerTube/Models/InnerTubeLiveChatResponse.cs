// Copyright (c) 2025 Agash Thamo. Licensed under the MIT License. See THIRD-PARTY-NOTICES.txt.

using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;

internal sealed record AccessibilityData
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

internal sealed record Accessibility
{
    [JsonPropertyName("accessibilityData")]
    public AccessibilityData? AccessibilityData { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

internal sealed record Icon
{
    [JsonPropertyName("iconType")]
    public string? IconType { get; init; }
}

internal sealed record SimpleText
{
    [JsonPropertyName("simpleText")]
    public string? Text { get; init; }
}

internal sealed record Thumbnail
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

internal sealed record ThumbnailList
{
    [JsonPropertyName("thumbnails")]
    public List<Thumbnail>? Thumbnails { get; init; }

    [JsonPropertyName("accessibility")]
    public Accessibility? Accessibility { get; init; }
}

[JsonConverter(typeof(MessageRunConverter))]
internal abstract record MessageRun;

internal sealed record MessageText : MessageRun
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("bold")]
    public bool Bold { get; init; }

    [JsonPropertyName("italics")]
    public bool Italics { get; init; }

    [JsonPropertyName("deemphasize")]
    public bool Deemphasize { get; init; }

    [JsonPropertyName("textColor")]
    public long? TextColor { get; init; }
}

internal sealed record MessageEmoji : MessageRun
{
    [JsonPropertyName("emoji")]
    public Emoji? Emoji { get; init; }

    [JsonPropertyName("isCustomEmoji")]
    public bool IsCustomEmoji { get; init; }
}

internal sealed record Message
{
    [JsonPropertyName("runs")]
    public List<MessageRun>? Runs { get; init; }
}

internal sealed record HeaderText
{
    [JsonPropertyName("simpleText")]
    public string? SimpleText { get; init; }

    [JsonPropertyName("runs")]
    public List<MessageRun>? Runs { get; init; }
}

internal sealed record CustomThumbnail
{
    [JsonPropertyName("thumbnails")]
    public List<Thumbnail>? Thumbnails { get; init; }
}

internal sealed record AuthorBadgeContainer
{
    [JsonPropertyName("liveChatAuthorBadgeRenderer")]
    public LiveChatAuthorBadgeRenderer? LiveChatAuthorBadgeRenderer { get; init; }
}

internal sealed record LiveChatAuthorBadgeRenderer
{
    [JsonPropertyName("customThumbnail")]
    public CustomThumbnail? CustomThumbnail { get; init; }

    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; init; }

    [JsonPropertyName("accessibility")]
    public Accessibility? Accessibility { get; init; }

    [JsonPropertyName("icon")]
    public Icon? Icon { get; init; }
}

internal sealed record EmojiImage
{
    [JsonPropertyName("thumbnails")]
    public List<Thumbnail>? Thumbnails { get; init; }

    [JsonPropertyName("accessibility")]
    public Accessibility? Accessibility { get; init; }
}

internal sealed record Emoji
{
    [JsonPropertyName("emojiId")]
    public string? EmojiId { get; init; }

    [JsonPropertyName("shortcuts")]
    public List<string>? Shortcuts { get; init; }

    [JsonPropertyName("searchTerms")]
    public List<string>? SearchTerms { get; init; }

    [JsonPropertyName("image")]
    public EmojiImage? Image { get; init; }

    [JsonPropertyName("isCustomEmoji")]
    public bool IsCustomEmoji { get; init; }
}

internal sealed record InvalidationContinuationData
{
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; }

    [JsonPropertyName("continuation")]
    public string? Continuation { get; init; }
}

internal sealed record TimedContinuationData
{
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; }

    [JsonPropertyName("continuation")]
    public string? Continuation { get; init; }
}

internal sealed record ReloadContinuationData
{
    [JsonPropertyName("continuation")]
    public string? Continuation { get; init; }
}

internal sealed record LiveChatReplayContinuationData
{
    [JsonPropertyName("timeUntilLastMessageMsec")]
    public int TimeUntilLastMessageMsec { get; init; }

    [JsonPropertyName("continuation")]
    public string? Continuation { get; init; }
}

internal sealed record Continuation
{
    [JsonPropertyName("invalidationContinuationData")]
    public InvalidationContinuationData? InvalidationContinuationData { get; init; }

    [JsonPropertyName("timedContinuationData")]
    public TimedContinuationData? TimedContinuationData { get; init; }

    [JsonPropertyName("reloadContinuationData")]
    public ReloadContinuationData? ReloadContinuationData { get; init; }

    [JsonPropertyName("liveChatReplayContinuationData")]
    public LiveChatReplayContinuationData? LiveChatReplayContinuationData { get; init; }
}

internal sealed record SubMenuItem
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("selected")]
    public bool Selected { get; init; }

    [JsonPropertyName("continuation")]
    public Continuation? Continuation { get; init; }
}

internal sealed record SortFilterSubMenuRenderer
{
    [JsonPropertyName("subMenuItems")]
    public List<SubMenuItem>? SubMenuItems { get; init; }
}

internal sealed record ViewSelector
{
    [JsonPropertyName("sortFilterSubMenuRenderer")]
    public SortFilterSubMenuRenderer? SortFilterSubMenuRenderer { get; init; }
}

internal sealed record LiveChatHeaderRenderer
{
    [JsonPropertyName("viewSelector")]
    public ViewSelector? ViewSelector { get; init; }
}

internal sealed record LiveChatHeader
{
    [JsonPropertyName("liveChatHeaderRenderer")]
    public LiveChatHeaderRenderer? LiveChatHeaderRenderer { get; init; }
}

internal record MessageRendererBase
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("timestampUsec")]
    public string? TimestampUsec { get; init; }

    [JsonPropertyName("authorName")]
    public SimpleText? AuthorName { get; init; }

    [JsonPropertyName("authorPhoto")]
    public ThumbnailList? AuthorPhoto { get; init; }

    [JsonPropertyName("authorBadges")]
    public List<AuthorBadgeContainer>? AuthorBadges { get; init; }

    [JsonPropertyName("authorExternalChannelId")]
    public string? AuthorExternalChannelId { get; init; }
}

internal sealed record BeforeContentButtonContainer
{
    [JsonPropertyName("buttonViewModel")]
    public ButtonViewModel? ButtonViewModel { get; init; }
}

internal sealed record ButtonViewModel
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

internal sealed record LiveChatTextMessageRenderer : MessageRendererBase
{
    [JsonPropertyName("message")]
    public Message? Message { get; init; }

    [JsonPropertyName("beforeContentButtons")]
    public List<BeforeContentButtonContainer>? BeforeContentButtons { get; init; }
}

internal sealed record PurchaseAmountText
{
    [JsonPropertyName("simpleText")]
    public string? Text { get; init; }
}

internal sealed record LeaderboardBadgeButtonViewModel
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

internal sealed record LeaderboardBadge
{
    [JsonPropertyName("buttonViewModel")]
    public LeaderboardBadgeButtonViewModel? ButtonViewModel { get; init; }
}

internal sealed record LiveChatPaidMessageRenderer : MessageRendererBase
{
    [JsonPropertyName("message")]
    public Message? Message { get; init; }

    [JsonPropertyName("purchaseAmountText")]
    public PurchaseAmountText? PurchaseAmountText { get; init; }

    [JsonPropertyName("headerBackgroundColor")]
    public long HeaderBackgroundColor { get; init; }

    [JsonPropertyName("headerTextColor")]
    public long HeaderTextColor { get; init; }

    [JsonPropertyName("bodyBackgroundColor")]
    public long BodyBackgroundColor { get; init; }

    [JsonPropertyName("bodyTextColor")]
    public long BodyTextColor { get; init; }

    [JsonPropertyName("authorNameTextColor")]
    public long AuthorNameTextColor { get; init; }

    [JsonPropertyName("leaderboardBadge")]
    public LeaderboardBadge? LeaderboardBadge { get; init; }
}

internal sealed record Sticker
{
    [JsonPropertyName("thumbnails")]
    public List<Thumbnail>? Thumbnails { get; init; }

    [JsonPropertyName("accessibility")]
    public Accessibility? Accessibility { get; init; }
}

internal sealed record LiveChatPaidStickerRenderer : MessageRendererBase
{
    [JsonPropertyName("sticker")]
    public Sticker? Sticker { get; init; }

    [JsonPropertyName("purchaseAmountText")]
    public PurchaseAmountText? PurchaseAmountText { get; init; }

    [JsonPropertyName("backgroundColor")]
    public long BackgroundColor { get; init; }

    [JsonPropertyName("authorNameTextColor")]
    public long AuthorNameTextColor { get; init; }
}

internal sealed record LiveChatMembershipItemRenderer : MessageRendererBase
{
    [JsonPropertyName("headerPrimaryText")]
    public HeaderText? HeaderPrimaryText { get; init; }

    [JsonPropertyName("headerSubtext")]
    public HeaderText? HeaderSubtext { get; init; }

    [JsonPropertyName("message")]
    public Message? Message { get; init; }

    [JsonPropertyName("empty")]
    public bool Empty { get; init; }
}

internal sealed record Source
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

internal sealed record IconSource
{
    [JsonPropertyName("sources")]
    public List<Source>? Sources { get; init; }
}

internal sealed record LiveChatSponsorshipsHeaderRenderer
{
    [JsonPropertyName("authorName")]
    public SimpleText? AuthorName { get; init; }

    [JsonPropertyName("authorPhoto")]
    public ThumbnailList? AuthorPhoto { get; init; }

    [JsonPropertyName("primaryText")]
    public HeaderText? PrimaryText { get; init; }

    [JsonPropertyName("authorBadges")]
    public List<AuthorBadgeContainer>? AuthorBadges { get; init; }
}

internal sealed record SponsorshipsHeaderContainer
{
    [JsonPropertyName("liveChatSponsorshipsHeaderRenderer")]
    public LiveChatSponsorshipsHeaderRenderer? LiveChatSponsorshipsHeaderRenderer { get; init; }
}

internal sealed record LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer : MessageRendererBase
{
    [JsonPropertyName("header")]
    public SponsorshipsHeaderContainer? Header { get; init; }
}

internal sealed record LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer : MessageRendererBase
{
    [JsonPropertyName("message")]
    public Message? Message { get; init; }
}

internal sealed record LiveChatPlaceholderItemRenderer
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("timestampUsec")]
    public string? TimestampUsec { get; init; }
}

internal sealed record ViewModelStyledText
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed record AvatarViewModel
{
    [JsonPropertyName("image")]
    public IconSource? Image { get; init; }
}

internal sealed record AvatarViewModelContainer
{
    [JsonPropertyName("avatarViewModel")]
    public AvatarViewModel? AvatarViewModel { get; init; }
}

internal sealed record GiftMessageViewModel
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("text")]
    public ViewModelStyledText? Text { get; init; }

    [JsonPropertyName("authorName")]
    public ViewModelStyledText? AuthorName { get; init; }

    [JsonPropertyName("giftImage")]
    public IconSource? GiftImage { get; init; }

    [JsonPropertyName("giftImageA11yLabel")]
    public string? GiftImageA11yLabel { get; init; }

    [JsonPropertyName("authorAvatar")]
    public AvatarViewModelContainer? AuthorAvatar { get; init; }
}

internal sealed record LiveChatViewerEngagementMessageRenderer
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("timestampUsec")]
    public string? TimestampUsec { get; init; }

    [JsonPropertyName("icon")]
    public Icon? Icon { get; init; }

    [JsonPropertyName("message")]
    public Message? Message { get; init; }
}

internal sealed record AddChatItemActionItem
{
    [JsonPropertyName("liveChatTextMessageRenderer")]
    public LiveChatTextMessageRenderer? LiveChatTextMessageRenderer { get; init; }

    [JsonPropertyName("liveChatPaidMessageRenderer")]
    public LiveChatPaidMessageRenderer? LiveChatPaidMessageRenderer { get; init; }

    [JsonPropertyName("liveChatMembershipItemRenderer")]
    public LiveChatMembershipItemRenderer? LiveChatMembershipItemRenderer { get; init; }

    [JsonPropertyName("liveChatPaidStickerRenderer")]
    public LiveChatPaidStickerRenderer? LiveChatPaidStickerRenderer { get; init; }

    [JsonPropertyName("liveChatSponsorshipsGiftPurchaseAnnouncementRenderer")]
    public LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer? LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer { get; init; }

    [JsonPropertyName("liveChatSponsorshipsGiftRedemptionAnnouncementRenderer")]
    public LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer? LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer { get; init; }

    [JsonPropertyName("liveChatPlaceholderItemRenderer")]
    public LiveChatPlaceholderItemRenderer? LiveChatPlaceholderItemRenderer { get; init; }

    [JsonPropertyName("liveChatViewerEngagementMessageRenderer")]
    public LiveChatViewerEngagementMessageRenderer? LiveChatViewerEngagementMessageRenderer { get; init; }

    [JsonPropertyName("giftMessageViewModel")]
    public GiftMessageViewModel? GiftMessageViewModel { get; init; }
}

internal sealed record AddChatItemAction
{
    [JsonPropertyName("item")]
    public AddChatItemActionItem? Item { get; init; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }
}

internal sealed record AddLiveChatTickerItemAction
{
    [JsonPropertyName("durationSec")]
    public string? DurationSec { get; init; }
}

internal sealed record RemoveChatItemAction
{
    [JsonPropertyName("targetItemId")]
    public string? TargetItemId { get; init; }
}

internal sealed record MarkChatItemAsDeletedAction
{
    [JsonPropertyName("targetItemId")]
    public string? TargetItemId { get; init; }
}

internal sealed record ReplacementItem
{
    [JsonPropertyName("liveChatTextMessageRenderer")]
    public LiveChatTextMessageRenderer? LiveChatTextMessageRenderer { get; init; }

    [JsonPropertyName("liveChatPlaceholderItemRenderer")]
    public LiveChatPlaceholderItemRenderer? LiveChatPlaceholderItemRenderer { get; init; }
}

internal sealed record ReplaceChatItemAction
{
    [JsonPropertyName("targetItemId")]
    public string? TargetItemId { get; init; }

    [JsonPropertyName("replacementItem")]
    public ReplacementItem? ReplacementItem { get; init; }
}

internal sealed record RemoveChatItemByAuthorAction
{
    [JsonPropertyName("externalChannelId")]
    public string? ExternalChannelId { get; init; }
}

internal sealed record ContinuationAction
{
    [JsonPropertyName("addChatItemAction")]
    public AddChatItemAction? AddChatItemAction { get; init; }

    [JsonPropertyName("removeChatItemAction")]
    public RemoveChatItemAction? RemoveChatItemAction { get; init; }

    [JsonPropertyName("markChatItemAsDeletedAction")]
    public MarkChatItemAsDeletedAction? MarkChatItemAsDeletedAction { get; init; }

    [JsonPropertyName("replaceChatItemAction")]
    public ReplaceChatItemAction? ReplaceChatItemAction { get; init; }

    [JsonPropertyName("removeChatItemByAuthorAction")]
    public RemoveChatItemByAuthorAction? RemoveChatItemByAuthorAction { get; init; }

    [JsonPropertyName("markChatItemsByAuthorAsDeletedAction")]
    public RemoveChatItemByAuthorAction? MarkChatItemsByAuthorAsDeletedAction { get; init; }

    [JsonPropertyName("addLiveChatTickerItemAction")]
    public AddLiveChatTickerItemAction? AddLiveChatTickerItemAction { get; init; }
}

internal sealed record LiveChatContinuation
{
    [JsonPropertyName("header")]
    public LiveChatHeader? Header { get; init; }

    [JsonPropertyName("continuations")]
    public List<Continuation>? Continuations { get; init; }

    [JsonPropertyName("actions")]
    public List<ContinuationAction>? Actions { get; init; }
}

internal sealed record ContinuationContents
{
    [JsonPropertyName("liveChatContinuation")]
    public LiveChatContinuation? LiveChatContinuation { get; init; }
}

internal sealed record LiveChatResponse
{
    [JsonPropertyName("continuationContents")]
    public ContinuationContents? ContinuationContents { get; init; }

    [JsonPropertyName("invalidationContinuationData")]
    public InvalidationContinuationData? InvalidationContinuationData { get; init; }
}
