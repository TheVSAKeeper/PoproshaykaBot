using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class YouTubeLiveBroadcastClient(
    IHttpClientFactory httpClientFactory,
    IYouTubeQuotaGovernor quotaGovernor,
    ILogger<YouTubeLiveBroadcastClient> logger)
    : IYouTubeLiveBroadcastClient
{
    private const int MaxBroadcastPages = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> ResolveChannelIdByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var normalized = handle.StartsWith('@') ? handle : "@" + handle;
        var uri = $"youtube/v3/channels?part=snippet&forHandle={Uri.EscapeDataString(normalized)}";

        var json = await SendBroadcasterGetAsync(uri, "channels.list", cancellationToken).ConfigureAwait(false);
        var dto = DeserializeResponse<ChannelsListResponseDto>(json, "channels.list");
        var channelId = dto?.Items?.FirstOrDefault()?.Id;

        return string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
    }

    public async Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(CancellationToken cancellationToken)
    {
        var results = new List<YouTubeBroadcastSummary>();
        string? pageToken = null;

        for (var page = 0; page < MaxBroadcastPages; page++)
        {
            var uri = "youtube/v3/liveBroadcasts?part=id,snippet,contentDetails,status&broadcastStatus=active&broadcastType=all&maxResults=50";

            if (!string.IsNullOrEmpty(pageToken))
            {
                uri += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var json = await SendBroadcasterGetAsync(uri, "liveBroadcasts.list", cancellationToken).ConfigureAwait(false);
            var dto = DeserializeResponse<LiveBroadcastsListResponseDto>(json, "liveBroadcasts.list");

            if (dto?.Items is not null)
            {
                foreach (var item in dto.Items)
                {
                    if (TryMapBroadcast(item, out var summary))
                    {
                        results.Add(summary);
                    }
                }
            }

            pageToken = dto?.NextPageToken;

            if (string.IsNullOrWhiteSpace(pageToken))
            {
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            logger.LogWarning(
                "YouTube liveBroadcasts.list достиг лимита страниц ({MaxPages}), часть трансляций могла не попасть в выборку",
                MaxBroadcastPages);
        }

        return results;
    }

    public async Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        var uri = $"youtube/v3/videos?part=snippet,liveStreamingDetails&id={Uri.EscapeDataString(videoId)}";
        var json = await SendBroadcasterGetAsync(uri, "videos.list", cancellationToken).ConfigureAwait(false);
        var dto = DeserializeResponse<VideosListResponseDto>(json, "videos.list");
        var item = dto?.Items?.FirstOrDefault();

        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            return null;
        }

        return new(
            item.Id,
            item.Snippet?.Title ?? string.Empty,
            item.LiveStreamingDetails?.ActualStartTime,
            item.LiveStreamingDetails?.ActualEndTime,
            ParseNullableInt(item.LiveStreamingDetails?.ConcurrentViewers));
    }

    private async Task<string> SendBroadcasterGetAsync(
        string relativeUri,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!quotaGovernor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out var lease))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                $"Локальный бюджет квоты YouTube исчерпан перед {operation}.",
                reason: "localBudgetReached");
        }

        using (lease)
        using (var client = httpClientFactory.CreateClient(YouTubeEndpoints.BroadcasterApiHttpClient))
        {
            HttpResponseMessage response;

            try
            {
                response = await client.GetAsync(relativeUri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw YouTubeErrorClassifier.FromTransportException(ex, operation);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw YouTubeErrorClassifier.FromTransportException(ex, operation);
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
                    throw YouTubeErrorClassifier.FromTransportException(ex, operation);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw YouTubeErrorClassifier.FromTransportException(ex, operation);
                }

                if (response.IsSuccessStatusCode)
                {
                    lease.Commit();
                    return body;
                }

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    lease.Commit();
                }

                throw YouTubeErrorClassifier.FromHttpResponse(response.StatusCode, body, operation);
            }
        }
    }

    private static T? DeserializeResponse<T>(string json, string operation)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw YouTubeErrorClassifier.FromTransportException(ex, operation);
        }
    }

    private static bool TryMapBroadcast(LiveBroadcastItemDto? item, out YouTubeBroadcastSummary summary)
    {
        summary = default!;

        if (item?.Snippet is null
            || string.IsNullOrWhiteSpace(item.Id)
            || string.IsNullOrWhiteSpace(item.Snippet.ChannelId)
            || string.IsNullOrWhiteSpace(item.Snippet.LiveChatId)
            || item.Snippet.ActualEndTime.HasValue)
        {
            return false;
        }

        var startedAt = item.Snippet.ActualStartTime ?? item.Snippet.PublishedAt ?? DateTimeOffset.UtcNow;

        summary = new(
            item.Id.Trim(),
            item.Snippet.ChannelId.Trim(),
            item.Snippet.ChannelTitle ?? string.Empty,
            item.Snippet.LiveChatId.Trim(),
            item.Snippet.Title ?? string.Empty,
            startedAt.ToUniversalTime());

        return true;
    }

    private static int? ParseNullableInt(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number >= 0 ? number : null;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed >= 0 ? parsed : null;
        }

        return null;
    }

    private sealed class ChannelsListResponseDto
    {
        [JsonPropertyName("items")]
        public List<ChannelItemDto>? Items { get; set; }
    }

    private sealed class ChannelItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class LiveBroadcastsListResponseDto
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("items")]
        public List<LiveBroadcastItemDto>? Items { get; set; }
    }

    private sealed class LiveBroadcastItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveBroadcastSnippetDto? Snippet { get; set; }
    }

    private sealed class LiveBroadcastSnippetDto
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("liveChatId")]
        public string? LiveChatId { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("actualStartTime")]
        public DateTimeOffset? ActualStartTime { get; set; }

        [JsonPropertyName("actualEndTime")]
        public DateTimeOffset? ActualEndTime { get; set; }
    }

    private sealed class VideosListResponseDto
    {
        [JsonPropertyName("items")]
        public List<VideoItemDto>? Items { get; set; }
    }

    private sealed class VideoItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public VideoSnippetDto? Snippet { get; set; }

        [JsonPropertyName("liveStreamingDetails")]
        public VideoLiveStreamingDetailsDto? LiveStreamingDetails { get; set; }
    }

    private sealed class VideoSnippetDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class VideoLiveStreamingDetailsDto
    {
        [JsonPropertyName("actualStartTime")]
        public DateTimeOffset? ActualStartTime { get; set; }

        [JsonPropertyName("actualEndTime")]
        public DateTimeOffset? ActualEndTime { get; set; }

        [JsonPropertyName("concurrentViewers")]
        public JsonElement? ConcurrentViewers { get; set; }
    }
}
