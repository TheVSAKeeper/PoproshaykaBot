using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class YouTubeLiveBroadcastClient(
    IHttpClientFactory httpClientFactory,
    IYouTubeQuotaGovernor quotaGovernor,
    ILogger<YouTubeLiveBroadcastClient> logger)
    : IYouTubeLiveBroadcastClient
{
    public const int YouTubeQuotaCost = 50;

    private const int MaxBroadcastPages = 10;
    private const string UpdateOperationName = "liveBroadcasts.update";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<string?> ResolveChannelIdByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        return ResolveChannelIdByHandleAsync(handle, PlatformAuthRole.Broadcaster, PlatformAuthRequestOptions.CurrentContextId, cancellationToken);
    }

    public async Task<string?> ResolveChannelIdByHandleAsync(
        string handle,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        using var _ = PlatformAuthRequestOptions.UseContext(contextId);

        var normalized = handle.StartsWith('@') ? handle : "@" + handle;
        var uri = $"youtube/v3/channels?part=snippet&forHandle={Uri.EscapeDataString(normalized)}";

        var json = await SendAuthorizedGetAsync(
                role,
                uri,
                "channels.list",
                YouTubeQuotaCategory.ChannelAndBroadcastInfo,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = DeserializeResponse<ChannelsListResponseDto>(json, "channels.list");
        var channelId = dto?.Items?.FirstOrDefault()?.Id;

        return string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
    }

    public Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(CancellationToken cancellationToken)
    {
        return ListActiveBroadcastsAsync(PlatformAuthRequestOptions.CurrentContextId, cancellationToken);
    }

    public Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(string? contextId, CancellationToken cancellationToken)
    {
        return ListActiveBroadcastsAsync(PlatformAuthRole.Broadcaster, contextId, cancellationToken);
    }

    public async Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        using var _ = PlatformAuthRequestOptions.UseContext(contextId);
        var results = new List<YouTubeBroadcastSummary>();
        string? pageToken = null;

        for (var page = 0; page < MaxBroadcastPages; page++)
        {
            var uri = "youtube/v3/liveBroadcasts?part=id,snippet,contentDetails,status&broadcastStatus=active&broadcastType=all&maxResults=50";

            if (!string.IsNullOrEmpty(pageToken))
            {
                uri += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var json = await SendAuthorizedGetAsync(
                    role,
                    uri,
                    "liveBroadcasts.list",
                    YouTubeQuotaCategory.StreamMonitoring,
                    cancellationToken)
                .ConfigureAwait(false);
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

    public Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, CancellationToken cancellationToken)
    {
        return GetVideoLiveSampleAsync(videoId, PlatformAuthRole.Broadcaster, PlatformAuthRequestOptions.CurrentContextId, cancellationToken);
    }

    public Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, string? contextId, CancellationToken cancellationToken)
    {
        return GetVideoLiveSampleAsync(videoId, PlatformAuthRole.Broadcaster, contextId, cancellationToken);
    }

    public async Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(
        string videoId,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        using var _ = PlatformAuthRequestOptions.UseContext(contextId);

        var uri = $"youtube/v3/videos?part=snippet,liveStreamingDetails&id={Uri.EscapeDataString(videoId)}";
        var json = await SendAuthorizedGetAsync(
                role,
                uri,
                "videos.list",
                YouTubeQuotaCategory.StreamMonitoring,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = DeserializeResponse<VideosListResponseDto>(json, "videos.list");
        var item = dto?.Items?.FirstOrDefault();

        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            return null;
        }

        var channelId = string.IsNullOrWhiteSpace(item.Snippet?.ChannelId) ? null : item.Snippet.ChannelId.Trim();
        var channelTitle = string.IsNullOrWhiteSpace(item.Snippet?.ChannelTitle) ? null : item.Snippet.ChannelTitle.Trim();
        var activeLiveChatId = string.IsNullOrWhiteSpace(item.LiveStreamingDetails?.ActiveLiveChatId)
            ? null
            : item.LiveStreamingDetails.ActiveLiveChatId.Trim();

        return new(
            item.Id,
            item.Snippet?.Title ?? string.Empty,
            item.LiveStreamingDetails?.ActualStartTime,
            item.LiveStreamingDetails?.ActualEndTime,
            ParseNullableInt(item.LiveStreamingDetails?.ConcurrentViewers),
            channelId,
            channelTitle,
            activeLiveChatId,
            HasLiveStreamingDetails: item.LiveStreamingDetails is not null);
    }

    public Task<YouTubeBroadcastUpdateResult> UpdateBroadcastMetadataAsync(
        YouTubeBroadcastUpdateRequest request,
        string? contextId,
        CancellationToken cancellationToken)
    {
        return UpdateBroadcastMetadataAsync(request, PlatformAuthRole.Broadcaster, contextId, cancellationToken);
    }

    public async Task<YouTubeBroadcastUpdateResult> UpdateBroadcastMetadataAsync(
        YouTubeBroadcastUpdateRequest request,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BroadcastId))
        {
            throw new ArgumentException("BroadcastId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Title is required.", nameof(request));
        }

        if (!quotaGovernor.TryReserve(
                role,
                YouTubeQuotaPriority.CriticalReadAndMonitor,
                YouTubeQuotaCost,
                out var lease,
                YouTubeQuotaCategory.ChannelAndBroadcastInfo))
        {
            return YouTubeBroadcastUpdateResult.Failure(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                "Исчерпан локальный бюджет квоты YouTube API");
        }

        var clientName = role == PlatformAuthRole.Bot
            ? YouTubeEndpoints.BotApiHttpClient
            : YouTubeEndpoints.BroadcasterApiHttpClient;

        using var _ = PlatformAuthRequestOptions.UseContext(contextId);
        using (lease)
        using (var client = httpClientFactory.CreateClient(clientName))
        {
            var privacyStatus = YouTubeBroadcastPrivacyStatus.Normalize(request.PrivacyStatus);
            var relativeUri = string.IsNullOrWhiteSpace(privacyStatus)
                ? "youtube/v3/liveBroadcasts?part=snippet"
                : "youtube/v3/liveBroadcasts?part=snippet,status";

            var payload = new LiveBroadcastUpdateRequestDto(
                request.BroadcastId.Trim(),
                new(
                    request.Title.Trim(),
                    request.Description,
                    request.ScheduledStartTimeUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                privacyStatus is null ? null : new LiveBroadcastUpdateStatusDto(privacyStatus));

            var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, relativeUri)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
            };
            httpRequest.Options.Set(PlatformAuthRequestOptions.ContextIdKey, AuthContextIds.Normalize(contextId));

            HttpResponseMessage response;

            try
            {
                response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (YouTubeApiException ex)
            {
                return YouTubeBroadcastUpdateResult.Failure(ex.Kind, FormatUpdateErrorMessage(ex));
            }
            catch (HttpRequestException ex)
            {
                var apiEx = YouTubeErrorClassifier.FromTransportException(ex, UpdateOperationName);
                return YouTubeBroadcastUpdateResult.Failure(apiEx.Kind, FormatUpdateErrorMessage(apiEx));
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var apiEx = YouTubeErrorClassifier.FromTransportException(ex, UpdateOperationName);
                return YouTubeBroadcastUpdateResult.Failure(apiEx.Kind, FormatUpdateErrorMessage(apiEx));
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
                    var transportEx = YouTubeErrorClassifier.FromTransportException(ex, UpdateOperationName);
                    return YouTubeBroadcastUpdateResult.Failure(transportEx.Kind, FormatUpdateErrorMessage(transportEx));
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var transportEx = YouTubeErrorClassifier.FromTransportException(ex, UpdateOperationName);
                    return YouTubeBroadcastUpdateResult.Failure(transportEx.Kind, FormatUpdateErrorMessage(transportEx));
                }

                if (response.IsSuccessStatusCode)
                {
                    lease.Commit();
                    return YouTubeBroadcastUpdateResult.Success();
                }

                var apiEx = YouTubeErrorClassifier.FromHttpResponse(response.StatusCode, body, UpdateOperationName);
                if (apiEx.Kind == YouTubeApiErrorKind.QuotaExceeded)
                {
                    lease.MarkExhaustedFromServer();
                }
                else if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    lease.Commit();
                }

                return YouTubeBroadcastUpdateResult.Failure(apiEx.Kind, FormatUpdateErrorMessage(apiEx));
            }
        }
    }

    private static string FormatUpdateErrorMessage(YouTubeApiException exception)
    {
        return exception.Kind switch
        {
            YouTubeApiErrorKind.Unauthorized => "Нет авторизации владельца канала YouTube",
            YouTubeApiErrorKind.Forbidden => "Недостаточно прав для изменения трансляции YouTube",
            YouTubeApiErrorKind.NotFound => "Активная трансляция YouTube не найдена",
            YouTubeApiErrorKind.QuotaExceeded => "Исчерпана квота YouTube API",
            YouTubeApiErrorKind.LocalQuotaBudgetReached => "Исчерпан локальный бюджет квоты YouTube API",
            YouTubeApiErrorKind.RateLimitExceeded => "Превышен лимит запросов к YouTube API",
            YouTubeApiErrorKind.BadRequest => "YouTube отклонил параметры трансляции",
            YouTubeApiErrorKind.Transient => "Сетевая ошибка при обновлении трансляции YouTube",
            _ => "Не удалось обновить трансляцию YouTube",
        };
    }

    private async Task<string> SendAuthorizedGetAsync(
        PlatformAuthRole role,
        string relativeUri,
        string operation,
        YouTubeQuotaCategory category,
        CancellationToken cancellationToken)
    {
        if (!quotaGovernor.TryReserve(role, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out var lease, category))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                $"Локальный бюджет квоты YouTube исчерпан перед {operation}.",
                reason: "localBudgetReached");
        }

        var clientName = role == PlatformAuthRole.Bot
            ? YouTubeEndpoints.BotApiHttpClient
            : YouTubeEndpoints.BroadcasterApiHttpClient;

        using (lease)
        using (var client = httpClientFactory.CreateClient(clientName))
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

                var apiEx = YouTubeErrorClassifier.FromHttpResponse(response.StatusCode, body, operation);
                if (apiEx.Kind == YouTubeApiErrorKind.QuotaExceeded)
                {
                    lease.MarkExhaustedFromServer();
                    throw new YouTubeApiException(
                        apiEx.Kind,
                        apiEx.Message,
                        apiEx.Reason,
                        apiEx.StatusCode,
                        apiEx.InnerException)
                    {
                        QuotaExhaustionRecorded = true,
                    };
                }

                throw apiEx;
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
            startedAt.ToUniversalTime(),
            item.Snippet.Description,
            YouTubeBroadcastPrivacyStatus.Normalize(item.Status?.PrivacyStatus),
            item.Snippet.ScheduledStartTime?.ToUniversalTime());

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

        [JsonPropertyName("status")]
        public LiveBroadcastStatusDto? Status { get; set; }
    }

    private sealed class LiveBroadcastSnippetDto
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("liveChatId")]
        public string? LiveChatId { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("scheduledStartTime")]
        public DateTimeOffset? ScheduledStartTime { get; set; }

        [JsonPropertyName("actualStartTime")]
        public DateTimeOffset? ActualStartTime { get; set; }

        [JsonPropertyName("actualEndTime")]
        public DateTimeOffset? ActualEndTime { get; set; }
    }

    private sealed class LiveBroadcastStatusDto
    {
        [JsonPropertyName("privacyStatus")]
        public string? PrivacyStatus { get; set; }
    }

    private sealed record LiveBroadcastUpdateRequestDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("snippet")] LiveBroadcastUpdateSnippetDto Snippet,
        [property: JsonPropertyName("status")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        LiveBroadcastUpdateStatusDto? Status);

    private sealed record LiveBroadcastUpdateSnippetDto(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Description,
        [property: JsonPropertyName("scheduledStartTime")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ScheduledStartTime);

    private sealed record LiveBroadcastUpdateStatusDto(
        [property: JsonPropertyName("privacyStatus")] string PrivacyStatus);

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
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

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

        [JsonPropertyName("activeLiveChatId")]
        public string? ActiveLiveChatId { get; set; }
    }
}
