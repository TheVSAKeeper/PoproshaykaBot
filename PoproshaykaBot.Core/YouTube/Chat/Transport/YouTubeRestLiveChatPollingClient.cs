using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed partial class YouTubeRestLiveChatPollingClient(
    IHttpClientFactory httpClientFactory,
    IYouTubeQuotaGovernor quotaGovernor,
    YouTubeChatReaderOptions? readerOptions = null)
    : IYouTubeLiveChatPollingClient
{
    private const int DefaultPageSize = 200;
    private const int MinimumRestPollingMs = 5_000;

    private readonly bool _retainRawPayloads = readerOptions?.RetainRawPayloads == true;

    public async Task<YouTubeLiveChatBatch> ListMessagesAsync(
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveChatId);

        if (!quotaGovernor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, YouTubeQuotaCosts.ChatReadUnits, out var lease, YouTubeQuotaCategory.ChatRead))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                "Локальный бюджет квоты YouTube исчерпан перед liveChatMessages.list.",
                reason: "localBudgetReached");
        }

        using (lease)
        using (var client = httpClientFactory.CreateClient(YouTubeEndpoints.BotApiHttpClient))
        {
            var uri = $"youtube/v3/liveChat/messages?liveChatId={Uri.EscapeDataString(liveChatId)}&part=id,snippet,authorDetails&maxResults={DefaultPageSize}";

            if (!string.IsNullOrEmpty(pageToken))
            {
                uri += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            HttpResponseMessage response;

            try
            {
                response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw YouTubeErrorClassifier.FromTransportException(ex, "liveChatMessages.list");
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw YouTubeErrorClassifier.FromTransportException(ex, "liveChatMessages.list");
            }

            using (response)
            {
                string body;

                try
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw YouTubeErrorClassifier.FromTransportException(ex, "liveChatMessages.list");
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw YouTubeErrorClassifier.FromTransportException(ex, "liveChatMessages.list");
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        lease.Commit();
                    }

                    throw YouTubeErrorClassifier.FromHttpResponse(response.StatusCode, body, "liveChatMessages.list");
                }

                lease.Commit();
                return MapResponse(body, _retainRawPayloads);
            }
        }
    }

    internal static YouTubeLiveChatBatch MapResponse(string json, bool retainRawPayloads = false)
    {
        LiveChatMessagesListResponseDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize(json, YouTubeRestLiveChatJsonContext.Default.LiveChatMessagesListResponseDto);
        }
        catch (JsonException ex)
        {
            throw YouTubeErrorClassifier.FromTransportException(ex, "liveChatMessages.list");
        }

        var items = new List<YouTubeLiveChatItem>(dto?.Items?.Count ?? 0);

        if (dto?.Items is not null)
        {
            foreach (var item in dto.Items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                var snippet = item.Snippet;
                var rawType = snippet?.Type ?? string.Empty;
                var kind = MapKind(rawType, snippet?.TextMessageDetails is not null);
                var channelId = item.AuthorDetails?.ChannelId ?? snippet?.AuthorChannelId ?? string.Empty;
                var avatarUrl = !string.IsNullOrWhiteSpace(item.AuthorDetails?.ProfileImageUrl)
                    ? item.AuthorDetails.ProfileImageUrl
                    : null;
                var rawPayloadJson = retainRawPayloads
                    ? JsonSerializer.Serialize(item, YouTubeRestLiveChatJsonContext.Default.LiveChatMessageDto)
                    : null;

                items.Add(new(
                    item.Id,
                    kind,
                    snippet?.PublishedAt?.ToUniversalTime(),
                    ExtractMessageText(snippet),
                    snippet?.DisplayMessage ?? string.Empty,
                    new(
                        channelId,
                        item.AuthorDetails?.DisplayName ?? string.Empty,
                        item.AuthorDetails?.IsChatOwner == true,
                        item.AuthorDetails?.IsChatModerator == true,
                        item.AuthorDetails?.IsChatSponsor == true,
                        item.AuthorDetails?.IsVerified == true,
                        avatarUrl),
                    rawType,
                    Monetary: MapMonetary(snippet),
                    Membership: MapMembership(snippet),
                    Moderation: MapModeration(item.Id, kind, snippet),
                    RawPayloadJson: rawPayloadJson));
            }
        }

        var intervalMs = Math.Max(MinimumRestPollingMs, dto?.PollingIntervalMillis ?? MinimumRestPollingMs);
        var nextPageToken = string.IsNullOrWhiteSpace(dto?.NextPageToken) ? null : dto.NextPageToken;
        var nextCursor = nextPageToken is null
            ? null
            : new YouTubeChatCursorToken(YouTubeChatTransportKind.OfficialRest, YouTubeChatMode.LiveChat, nextPageToken);

        return new(
            items,
            nextPageToken,
            TimeSpan.FromMilliseconds(intervalMs),
            dto?.OfflineAt?.ToUniversalTime(),
            nextCursor,
            YouTubeChatTransportKind.OfficialRest);
    }

    private static string ExtractMessageText(LiveChatMessageSnippetDto? snippet)
    {
        if (snippet is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(snippet.TextMessageDetails?.MessageText))
        {
            return snippet.TextMessageDetails.MessageText;
        }

        if (!string.IsNullOrEmpty(snippet.SuperChatDetails?.UserComment))
        {
            return snippet.SuperChatDetails.UserComment;
        }

        if (!string.IsNullOrEmpty(snippet.FanFundingEventDetails?.UserComment))
        {
            return snippet.FanFundingEventDetails.UserComment;
        }

        return snippet.MemberMilestoneChatDetails?.UserComment ?? string.Empty;
    }

    private static YouTubeMonetaryDetails? MapMonetary(LiveChatMessageSnippetDto? snippet)
    {
        if (snippet is null)
        {
            return null;
        }

        if (snippet.SuperChatDetails is { } sc)
        {
            return new(
                sc.AmountMicros,
                sc.AmountMicros.HasValue ? sc.AmountMicros.Value / 1_000_000m : null,
                sc.Currency,
                sc.AmountDisplayString ?? string.Empty,
                sc.Tier);
        }

        if (snippet.SuperStickerDetails is { } ss)
        {
            return new(
                ss.AmountMicros,
                ss.AmountMicros.HasValue ? ss.AmountMicros.Value / 1_000_000m : null,
                ss.Currency,
                ss.AmountDisplayString ?? string.Empty,
                ss.Tier,
                StickerId: ss.SuperStickerMetadata?.StickerId,
                StickerAltText: ss.SuperStickerMetadata?.AltText);
        }

        if (snippet.FanFundingEventDetails is { } ff)
        {
            return new(
                ff.AmountMicros,
                ff.AmountMicros.HasValue ? ff.AmountMicros.Value / 1_000_000m : null,
                ff.Currency,
                ff.AmountDisplayString ?? string.Empty);
        }

        return null;
    }

    private static YouTubeMembershipDetails? MapMembership(LiveChatMessageSnippetDto? snippet)
    {
        if (snippet is null)
        {
            return null;
        }

        if (snippet.NewSponsorDetails is { } ns)
        {
            return new(
                ns.IsUpgrade == true ? YouTubeMembershipEventKind.Upgraded : YouTubeMembershipEventKind.NewMember,
                ns.MemberLevelName);
        }

        if (snippet.MemberMilestoneChatDetails is { } mm)
        {
            return new(
                YouTubeMembershipEventKind.Milestone,
                mm.MemberLevelName,
                mm.MemberMonth);
        }

        if (snippet.MembershipGiftingDetails is { } mg)
        {
            return new(
                YouTubeMembershipEventKind.GiftPurchase,
                mg.GiftMembershipsLevelName,
                GiftCount: mg.GiftMembershipsCount);
        }

        if (snippet.GiftMembershipReceivedDetails is { } gm)
        {
            return new(
                YouTubeMembershipEventKind.GiftRedemption,
                gm.MemberLevelName,
                GifterChannelId: gm.GifterChannelId,
                AssociatedGiftingMessageId: gm.AssociatedMembershipGiftingMessageId);
        }

        return null;
    }

    private static YouTubeModerationDetails? MapModeration(
        string messageId,
        YouTubeLiveChatItemKind kind,
        LiveChatMessageSnippetDto? snippet)
    {
        if (snippet?.MessageDeletedDetails is { } deleted)
        {
            return new(
                YouTubeModerationActionKind.MessageDeleted,
                TargetMessageId: string.IsNullOrWhiteSpace(deleted.DeletedMessageId) ? messageId : deleted.DeletedMessageId);
        }

        if (kind == YouTubeLiveChatItemKind.Tombstone)
        {
            return new(YouTubeModerationActionKind.MessageDeleted, TargetMessageId: messageId);
        }

        if (snippet?.UserBannedDetails is { } banned)
        {
            var isTemporary = string.Equals(banned.BanType, "temporary", StringComparison.OrdinalIgnoreCase);
            var duration = isTemporary && banned.BanDurationSeconds is > 0
                ? TimeSpan.FromSeconds(banned.BanDurationSeconds.Value)
                : (TimeSpan?)null;

            return new(
                isTemporary
                    ? YouTubeModerationActionKind.UserBannedTemporary
                    : YouTubeModerationActionKind.UserBannedPermanent,
                TargetChannelId: banned.BannedUserDetails?.ChannelId,
                BanDuration: duration);
        }

        return null;
    }

    private static YouTubeLiveChatItemKind MapKind(string rawType, bool hasTextDetails)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return hasTextDetails ? YouTubeLiveChatItemKind.TextMessage : YouTubeLiveChatItemKind.Other;
        }

        return rawType switch
        {
            "textMessageEvent" => YouTubeLiveChatItemKind.TextMessage,
            "superChatEvent" => YouTubeLiveChatItemKind.SuperChat,
            "superStickerEvent" => YouTubeLiveChatItemKind.SuperSticker,
            "fanFundingEvent" => YouTubeLiveChatItemKind.FanFunding,
            "newSponsorEvent" => YouTubeLiveChatItemKind.NewSponsor,
            "memberMilestoneChatEvent" => YouTubeLiveChatItemKind.MemberMilestoneChat,
            "membershipGiftingEvent" => YouTubeLiveChatItemKind.MembershipGifting,
            "giftMembershipReceivedEvent" => YouTubeLiveChatItemKind.GiftMembershipReceived,
            "giftEvent" => YouTubeLiveChatItemKind.Gift,
            "chatEndedEvent" => YouTubeLiveChatItemKind.ChatEnded,
            "tombstone" => YouTubeLiveChatItemKind.Tombstone,
            "userBannedEvent" => YouTubeLiveChatItemKind.UserBanned,
            "pollEvent" => YouTubeLiveChatItemKind.Poll,
            _ => hasTextDetails ? YouTubeLiveChatItemKind.TextMessage : YouTubeLiveChatItemKind.Other,
        };
    }

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(LiveChatMessagesListResponseDto))]
    [JsonSerializable(typeof(LiveChatMessageDto))]
    private sealed partial class YouTubeRestLiveChatJsonContext : JsonSerializerContext;

    internal sealed class LiveChatMessagesListResponseDto
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("pollingIntervalMillis")]
        public int? PollingIntervalMillis { get; set; }

        [JsonPropertyName("offlineAt")]
        public DateTimeOffset? OfflineAt { get; set; }

        [JsonPropertyName("items")]
        public List<LiveChatMessageDto>? Items { get; set; }
    }

    internal sealed class LiveChatMessageDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveChatMessageSnippetDto? Snippet { get; set; }

        [JsonPropertyName("authorDetails")]
        public LiveChatMessageAuthorDetailsDto? AuthorDetails { get; set; }
    }

    internal sealed class LiveChatMessageSnippetDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("authorChannelId")]
        public string? AuthorChannelId { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("displayMessage")]
        public string? DisplayMessage { get; set; }

        [JsonPropertyName("textMessageDetails")]
        public LiveChatTextMessageDetailsDto? TextMessageDetails { get; set; }

        [JsonPropertyName("superChatDetails")]
        public LiveChatSuperChatDetailsDto? SuperChatDetails { get; set; }

        [JsonPropertyName("superStickerDetails")]
        public LiveChatSuperStickerDetailsDto? SuperStickerDetails { get; set; }

        [JsonPropertyName("fanFundingEventDetails")]
        public LiveChatFanFundingEventDetailsDto? FanFundingEventDetails { get; set; }

        [JsonPropertyName("newSponsorDetails")]
        public LiveChatNewSponsorDetailsDto? NewSponsorDetails { get; set; }

        [JsonPropertyName("memberMilestoneChatDetails")]
        public LiveChatMemberMilestoneChatDetailsDto? MemberMilestoneChatDetails { get; set; }

        [JsonPropertyName("membershipGiftingDetails")]
        public LiveChatMembershipGiftingDetailsDto? MembershipGiftingDetails { get; set; }

        [JsonPropertyName("giftMembershipReceivedDetails")]
        public LiveChatGiftMembershipReceivedDetailsDto? GiftMembershipReceivedDetails { get; set; }

        [JsonPropertyName("messageDeletedDetails")]
        public LiveChatMessageDeletedDetailsDto? MessageDeletedDetails { get; set; }

        [JsonPropertyName("userBannedDetails")]
        public LiveChatUserBannedDetailsDto? UserBannedDetails { get; set; }
    }

    internal sealed class LiveChatTextMessageDetailsDto
    {
        [JsonPropertyName("messageText")]
        public string? MessageText { get; set; }
    }

    internal sealed class LiveChatSuperChatDetailsDto
    {
        [JsonPropertyName("amountMicros")]
        public long? AmountMicros { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amountDisplayString")]
        public string? AmountDisplayString { get; set; }

        [JsonPropertyName("userComment")]
        public string? UserComment { get; set; }

        [JsonPropertyName("tier")]
        public uint? Tier { get; set; }
    }

    internal sealed class LiveChatSuperStickerDetailsDto
    {
        [JsonPropertyName("amountMicros")]
        public long? AmountMicros { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amountDisplayString")]
        public string? AmountDisplayString { get; set; }

        [JsonPropertyName("tier")]
        public uint? Tier { get; set; }

        [JsonPropertyName("superStickerMetadata")]
        public LiveChatSuperStickerMetadataDto? SuperStickerMetadata { get; set; }
    }

    internal sealed class LiveChatSuperStickerMetadataDto
    {
        [JsonPropertyName("stickerId")]
        public string? StickerId { get; set; }

        [JsonPropertyName("altText")]
        public string? AltText { get; set; }
    }

    internal sealed class LiveChatFanFundingEventDetailsDto
    {
        [JsonPropertyName("amountMicros")]
        public long? AmountMicros { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amountDisplayString")]
        public string? AmountDisplayString { get; set; }

        [JsonPropertyName("userComment")]
        public string? UserComment { get; set; }
    }

    internal sealed class LiveChatNewSponsorDetailsDto
    {
        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }

        [JsonPropertyName("isUpgrade")]
        public bool? IsUpgrade { get; set; }
    }

    internal sealed class LiveChatMemberMilestoneChatDetailsDto
    {
        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }

        [JsonPropertyName("memberMonth")]
        public int? MemberMonth { get; set; }

        [JsonPropertyName("userComment")]
        public string? UserComment { get; set; }
    }

    internal sealed class LiveChatMembershipGiftingDetailsDto
    {
        [JsonPropertyName("giftMembershipsCount")]
        public int? GiftMembershipsCount { get; set; }

        [JsonPropertyName("giftMembershipsLevelName")]
        public string? GiftMembershipsLevelName { get; set; }
    }

    internal sealed class LiveChatGiftMembershipReceivedDetailsDto
    {
        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }

        [JsonPropertyName("gifterChannelId")]
        public string? GifterChannelId { get; set; }

        [JsonPropertyName("associatedMembershipGiftingMessageId")]
        public string? AssociatedMembershipGiftingMessageId { get; set; }
    }

    internal sealed class LiveChatMessageDeletedDetailsDto
    {
        [JsonPropertyName("deletedMessageId")]
        public string? DeletedMessageId { get; set; }
    }

    internal sealed class LiveChatUserBannedDetailsDto
    {
        [JsonPropertyName("bannedUserDetails")]
        public LiveChatBannedUserDetailsDto? BannedUserDetails { get; set; }

        [JsonPropertyName("banType")]
        public string? BanType { get; set; }

        [JsonPropertyName("banDurationSeconds")]
        public long? BanDurationSeconds { get; set; }
    }

    internal sealed class LiveChatBannedUserDetailsDto
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }
    }

    internal sealed class LiveChatMessageAuthorDetailsDto
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("profileImageUrl")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("isChatOwner")]
        public bool IsChatOwner { get; set; }

        [JsonPropertyName("isChatModerator")]
        public bool IsChatModerator { get; set; }

        [JsonPropertyName("isChatSponsor")]
        public bool IsChatSponsor { get; set; }

        [JsonPropertyName("isVerified")]
        public bool IsVerified { get; set; }
    }
}
