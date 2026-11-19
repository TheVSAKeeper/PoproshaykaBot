using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Chat.Proto;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Globalization;
using System.Runtime.CompilerServices;
using ProtoMessageType = PoproshaykaBot.Core.YouTube.Chat.Proto.LiveChatMessageSnippet.Types.TypeWrapper.Types.Type;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class YouTubeGrpcLiveChatStreamClient(
    YouTubeGrpcChannelProvider channelProvider,
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    IYouTubeQuotaGovernor quotaGovernor,
    ILogger<YouTubeGrpcLiveChatStreamClient> logger)
    : IYouTubeLiveChatStreamClient
{
    private const uint DefaultPageSize = 200;
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(5);

    public async IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
        string liveChatId,
        string? pageToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveChatId);

        var botContextId = PlatformAuthRequestOptions.CurrentContextId;
        var token = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Unauthorized,
                "Токен YouTube для роли Bot отсутствует или недействителен.");
        }

        if (!quotaGovernor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var lease))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                "Локальный бюджет квоты YouTube исчерпан перед StreamList.",
                reason: "localBudgetReached");
        }

        using (lease)
        {
            var client = new V3DataLiveChatMessageService.V3DataLiveChatMessageServiceClient(channelProvider.GetChannel());
            var request = new LiveChatMessageListRequest
            {
                LiveChatId = liveChatId,
                MaxResults = DefaultPageSize,
            };
            request.Part.Add("id");
            request.Part.Add("snippet");
            request.Part.Add("authorDetails");

            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }

            var headers = new Metadata
            {
                { "authorization", $"Bearer {token}" },
            };

            using var call = client.StreamList(request, headers, cancellationToken: cancellationToken);

            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
                }
                catch (RpcException ex)
                {
                    if (ex.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (ex.StatusCode == StatusCode.Unauthenticated)
                    {
                        authService.TryClearAccessToken(PlatformAuthRole.Bot, botContextId, token);
                        logger.LogWarning(ex, "YouTube StreamList вернул Unauthenticated – очищаем сохранённый токен роли Bot");
                    }

                    var classified = YouTubeErrorClassifier.FromRpcException(ex, "StreamList");

                    if (ShouldCommitOnError(classified.Kind))
                    {
                        lease.Commit();
                    }

                    throw classified;
                }
                catch (HttpRequestException ex)
                {
                    throw YouTubeErrorClassifier.FromTransportException(ex, "StreamList");
                }

                if (!hasNext)
                {
                    break;
                }

                lease.Commit();
                yield return MapResponse(call.ResponseStream.Current);
            }
        }
    }

    private static bool ShouldCommitOnError(YouTubeApiErrorKind kind)
    {
        return kind is YouTubeApiErrorKind.InvalidPageToken
            or YouTubeApiErrorKind.LiveChatEnded
            or YouTubeApiErrorKind.BadRequest
            or YouTubeApiErrorKind.Forbidden
            or YouTubeApiErrorKind.NotFound
            or YouTubeApiErrorKind.QuotaExceeded
            or YouTubeApiErrorKind.RateLimitExceeded;
    }

    private static YouTubeLiveChatBatch MapResponse(LiveChatMessageListResponse response)
    {
        var items = new List<YouTubeLiveChatItem>(response.Items.Count);

        foreach (var proto in response.Items)
        {
            if (proto is null || string.IsNullOrWhiteSpace(proto.Id))
            {
                continue;
            }

            var snippet = proto.Snippet;
            var kind = MapKind(snippet?.Type ?? ProtoMessageType.InvalidType);
            var publishedAt = TryParseTimestamp(snippet?.HasPublishedAt == true ? snippet.PublishedAt : null);
            var author = proto.AuthorDetails;
            var channelId = author?.ChannelId ?? snippet?.AuthorChannelId ?? string.Empty;
            var displayName = author?.DisplayName ?? string.Empty;
            var avatarUrl = author?.HasProfileImageUrl == true && !string.IsNullOrWhiteSpace(author.ProfileImageUrl)
                ? author.ProfileImageUrl
                : null;

            items.Add(new(
                proto.Id,
                kind,
                publishedAt,
                ExtractMessageText(snippet),
                snippet?.DisplayMessage ?? string.Empty,
                new(
                    channelId,
                    displayName,
                    author?.IsChatOwner == true,
                    author?.IsChatModerator == true,
                    author?.IsChatSponsor == true,
                    author?.IsVerified == true,
                    avatarUrl),
                (snippet?.Type ?? ProtoMessageType.InvalidType).ToString(),
                Monetary: MapMonetary(snippet),
                Membership: MapMembership(snippet),
                Moderation: MapModeration(proto.Id, kind, snippet)));
        }

        var interval = response.HasPollingIntervalMillis && response.PollingIntervalMillis > 0
            ? TimeSpan.FromMilliseconds(response.PollingIntervalMillis)
            : DefaultPollingInterval;

        var offlineAt = TryParseTimestamp(response.HasOfflineAt ? response.OfflineAt : null);
        var nextPageToken = response.HasNextPageToken && !string.IsNullOrWhiteSpace(response.NextPageToken)
            ? response.NextPageToken
            : null;
        var nextCursor = nextPageToken is null
            ? null
            : new YouTubeChatCursorToken(YouTubeChatTransportKind.OfficialGrpc, YouTubeChatMode.LiveChat, nextPageToken);

        return new(
            items,
            nextPageToken,
            interval,
            offlineAt,
            nextCursor,
            YouTubeChatTransportKind.OfficialGrpc);
    }

    private static string ExtractMessageText(LiveChatMessageSnippet? snippet)
    {
        if (snippet is null)
        {
            return string.Empty;
        }

        return snippet.DisplayedContentCase switch
        {
            LiveChatMessageSnippet.DisplayedContentOneofCase.TextMessageDetails =>
                snippet.TextMessageDetails?.MessageText ?? string.Empty,
            LiveChatMessageSnippet.DisplayedContentOneofCase.SuperChatDetails =>
                snippet.SuperChatDetails?.UserComment ?? string.Empty,
            LiveChatMessageSnippet.DisplayedContentOneofCase.MemberMilestoneChatDetails =>
                snippet.MemberMilestoneChatDetails?.UserComment ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static YouTubeMonetaryDetails? MapMonetary(LiveChatMessageSnippet? snippet)
    {
        if (snippet is null)
        {
            return null;
        }

        switch (snippet.DisplayedContentCase)
        {
            case LiveChatMessageSnippet.DisplayedContentOneofCase.SuperChatDetails when snippet.SuperChatDetails is { } sc:
            {
                var micros = sc.HasAmountMicros && sc.AmountMicros <= long.MaxValue ? (long)sc.AmountMicros : (long?)null;
                return new(
                    micros,
                    micros.HasValue ? micros.Value / 1_000_000m : null,
                    sc.HasCurrency ? sc.Currency : null,
                    sc.HasAmountDisplayString ? sc.AmountDisplayString : string.Empty,
                    sc.HasTier ? sc.Tier : null);
            }
            case LiveChatMessageSnippet.DisplayedContentOneofCase.SuperStickerDetails when snippet.SuperStickerDetails is { } ss:
            {
                var micros = ss.HasAmountMicros && ss.AmountMicros <= long.MaxValue ? (long)ss.AmountMicros : (long?)null;
                return new(
                    micros,
                    micros.HasValue ? micros.Value / 1_000_000m : null,
                    ss.HasCurrency ? ss.Currency : null,
                    ss.HasAmountDisplayString ? ss.AmountDisplayString : string.Empty,
                    ss.HasTier ? ss.Tier : null,
                    StickerId: ss.SuperStickerMetadata?.HasStickerId == true ? ss.SuperStickerMetadata.StickerId : null,
                    StickerAltText: ss.SuperStickerMetadata?.HasAltText == true ? ss.SuperStickerMetadata.AltText : null);
            }
            case LiveChatMessageSnippet.DisplayedContentOneofCase.GiftDetails when snippet.GiftDetails is { } gd:
                return new(
                    null,
                    null,
                    null,
                    gd.HasAltText ? gd.AltText : string.Empty,
                    StickerImageUrl: gd.HasGiftUrl ? gd.GiftUrl : null,
                    JewelsAmount: gd.HasJewelsAmount ? gd.JewelsAmount : null);
            default:
                return null;
        }
    }

    private static YouTubeMembershipDetails? MapMembership(LiveChatMessageSnippet? snippet)
    {
        if (snippet is null)
        {
            return null;
        }

        return snippet.DisplayedContentCase switch
        {
            LiveChatMessageSnippet.DisplayedContentOneofCase.NewSponsorDetails when snippet.NewSponsorDetails is { } ns =>
                new(
                    ns.HasIsUpgrade && ns.IsUpgrade
                        ? YouTubeMembershipEventKind.Upgraded
                        : YouTubeMembershipEventKind.NewMember,
                    ns.HasMemberLevelName ? ns.MemberLevelName : null),
            LiveChatMessageSnippet.DisplayedContentOneofCase.MemberMilestoneChatDetails when snippet.MemberMilestoneChatDetails is { } mm =>
                new(
                    YouTubeMembershipEventKind.Milestone,
                    mm.HasMemberLevelName ? mm.MemberLevelName : null,
                    mm.HasMemberMonth ? checked((int)mm.MemberMonth) : null),
            LiveChatMessageSnippet.DisplayedContentOneofCase.MembershipGiftingDetails when snippet.MembershipGiftingDetails is { } mg =>
                new(
                    YouTubeMembershipEventKind.GiftPurchase,
                    mg.HasGiftMembershipsLevelName ? mg.GiftMembershipsLevelName : null,
                    GiftCount: mg.HasGiftMembershipsCount ? mg.GiftMembershipsCount : null),
            LiveChatMessageSnippet.DisplayedContentOneofCase.GiftMembershipReceivedDetails when snippet.GiftMembershipReceivedDetails is { } gm =>
                new(
                    YouTubeMembershipEventKind.GiftRedemption,
                    gm.HasMemberLevelName ? gm.MemberLevelName : null,
                    GifterChannelId: gm.HasGifterChannelId ? gm.GifterChannelId : null,
                    AssociatedGiftingMessageId: gm.HasAssociatedMembershipGiftingMessageId
                        ? gm.AssociatedMembershipGiftingMessageId
                        : null),
            _ => null,
        };
    }

    private static YouTubeModerationDetails? MapModeration(
        string messageId,
        YouTubeLiveChatItemKind kind,
        LiveChatMessageSnippet? snippet)
    {
        if (kind == YouTubeLiveChatItemKind.Tombstone)
        {
            return new(YouTubeModerationActionKind.MessageDeleted, TargetMessageId: messageId);
        }

        if (snippet?.DisplayedContentCase == LiveChatMessageSnippet.DisplayedContentOneofCase.UserBannedDetails
            && snippet.UserBannedDetails is { } banned)
        {
            var isTemporary = banned.HasBanType
                && banned.BanType == LiveChatUserBannedMessageDetails.Types.BanTypeWrapper.Types.BanType.Temporary;
            var duration = isTemporary && banned.HasBanDurationSeconds
                ? TimeSpan.FromSeconds(banned.BanDurationSeconds)
                : (TimeSpan?)null;

            return new(
                isTemporary
                    ? YouTubeModerationActionKind.UserBannedTemporary
                    : YouTubeModerationActionKind.UserBannedPermanent,
                TargetChannelId: banned.BannedUserDetails?.HasChannelId == true ? banned.BannedUserDetails.ChannelId : null,
                BanDuration: duration);
        }

        return null;
    }

    private static YouTubeLiveChatItemKind MapKind(ProtoMessageType type)
    {
        return type switch
        {
            ProtoMessageType.TextMessageEvent => YouTubeLiveChatItemKind.TextMessage,
            ProtoMessageType.SuperChatEvent => YouTubeLiveChatItemKind.SuperChat,
            ProtoMessageType.SuperStickerEvent => YouTubeLiveChatItemKind.SuperSticker,
            ProtoMessageType.FanFundingEvent => YouTubeLiveChatItemKind.FanFunding,
            ProtoMessageType.NewSponsorEvent => YouTubeLiveChatItemKind.NewSponsor,
            ProtoMessageType.MemberMilestoneChatEvent => YouTubeLiveChatItemKind.MemberMilestoneChat,
            ProtoMessageType.MembershipGiftingEvent => YouTubeLiveChatItemKind.MembershipGifting,
            ProtoMessageType.GiftMembershipReceivedEvent => YouTubeLiveChatItemKind.GiftMembershipReceived,
            ProtoMessageType.GiftEvent => YouTubeLiveChatItemKind.Gift,
            ProtoMessageType.ChatEndedEvent => YouTubeLiveChatItemKind.ChatEnded,
            ProtoMessageType.Tombstone => YouTubeLiveChatItemKind.Tombstone,
            ProtoMessageType.UserBannedEvent => YouTubeLiveChatItemKind.UserBanned,
            ProtoMessageType.PollEvent => YouTubeLiveChatItemKind.Poll,
            _ => YouTubeLiveChatItemKind.Other,
        };
    }

    private static DateTimeOffset? TryParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
