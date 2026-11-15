using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class YouTubeRestLiveChatPollingClient(
    IHttpClientFactory httpClientFactory,
    IYouTubeQuotaGovernor quotaGovernor)
    : IYouTubeLiveChatPollingClient
{
    private const int DefaultPageSize = 200;
    private const int MinimumRestPollingMs = 5_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<YouTubeLiveChatBatch> ListMessagesAsync(
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveChatId);

        if (!quotaGovernor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var lease))
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
                return MapResponse(body);
            }
        }
    }

    internal static YouTubeLiveChatBatch MapResponse(string json)
    {
        LiveChatMessagesListResponseDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<LiveChatMessagesListResponseDto>(json, JsonOptions);
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

                var rawType = item.Snippet?.Type ?? string.Empty;
                var kind = MapKind(rawType, item.Snippet?.TextMessageDetails is not null);
                var channelId = item.AuthorDetails?.ChannelId ?? item.Snippet?.AuthorChannelId ?? string.Empty;

                items.Add(new(
                    item.Id,
                    kind,
                    item.Snippet?.PublishedAt?.ToUniversalTime(),
                    item.Snippet?.TextMessageDetails?.MessageText ?? string.Empty,
                    item.Snippet?.DisplayMessage ?? string.Empty,
                    new(
                        channelId,
                        item.AuthorDetails?.DisplayName ?? string.Empty,
                        item.AuthorDetails?.IsChatOwner == true,
                        item.AuthorDetails?.IsChatModerator == true,
                        item.AuthorDetails?.IsChatSponsor == true,
                        item.AuthorDetails?.IsVerified == true),
                    rawType));
            }
        }

        var intervalMs = Math.Max(MinimumRestPollingMs, dto?.PollingIntervalMillis ?? MinimumRestPollingMs);

        return new(
            items,
            string.IsNullOrWhiteSpace(dto?.NextPageToken) ? null : dto.NextPageToken,
            TimeSpan.FromMilliseconds(intervalMs),
            dto?.OfflineAt?.ToUniversalTime());
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

    private sealed class LiveChatMessagesListResponseDto
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

    private sealed class LiveChatMessageDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveChatMessageSnippetDto? Snippet { get; set; }

        [JsonPropertyName("authorDetails")]
        public LiveChatMessageAuthorDetailsDto? AuthorDetails { get; set; }
    }

    private sealed class LiveChatMessageSnippetDto
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
    }

    private sealed class LiveChatTextMessageDetailsDto
    {
        [JsonPropertyName("messageText")]
        public string? MessageText { get; set; }
    }

    private sealed class LiveChatMessageAuthorDetailsDto
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

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
