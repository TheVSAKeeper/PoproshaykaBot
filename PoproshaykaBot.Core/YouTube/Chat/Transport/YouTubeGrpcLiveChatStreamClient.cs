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

        var token = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, forceRefresh: false, cancellationToken)
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
                        authService.TryClearAccessToken(PlatformAuthRole.Bot, token);
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

            var kind = MapKind(proto.Snippet?.Type ?? ProtoMessageType.InvalidType);
            var publishedAt = TryParseTimestamp(proto.Snippet?.HasPublishedAt == true ? proto.Snippet.PublishedAt : null);
            var author = proto.AuthorDetails;
            var channelId = author?.ChannelId ?? proto.Snippet?.AuthorChannelId ?? string.Empty;
            var displayName = author?.DisplayName ?? string.Empty;

            items.Add(new(
                proto.Id,
                kind,
                publishedAt,
                proto.Snippet?.TextMessageDetails?.MessageText ?? string.Empty,
                proto.Snippet?.DisplayMessage ?? string.Empty,
                new(
                    channelId,
                    displayName,
                    author?.IsChatOwner == true,
                    author?.IsChatModerator == true,
                    author?.IsChatSponsor == true,
                    author?.IsVerified == true),
                (proto.Snippet?.Type ?? ProtoMessageType.InvalidType).ToString()));
        }

        var interval = response.HasPollingIntervalMillis && response.PollingIntervalMillis > 0
            ? TimeSpan.FromMilliseconds(response.PollingIntervalMillis)
            : DefaultPollingInterval;

        var offlineAt = TryParseTimestamp(response.HasOfflineAt ? response.OfflineAt : null);

        return new(
            items,
            response.HasNextPageToken && !string.IsNullOrWhiteSpace(response.NextPageToken) ? response.NextPageToken : null,
            interval,
            offlineAt);
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
