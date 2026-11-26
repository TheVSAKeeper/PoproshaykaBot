using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeChannelIdentityClient(
    IHttpClientFactory httpClientFactory,
    YouTubeOAuthOptions options,
    ILogger<YouTubeChannelIdentityClient> logger)
    : IYouTubeChannelIdentityClient
{
    public async Task<YouTubeChannelIdentity> GetOwnChannelAsync(string accessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        return await FetchFirstChannelAsync("part=snippet&mine=true", accessToken, ct).ConfigureAwait(false)
            ?? throw new YouTubeOAuthException("У выбранного Google-аккаунта не найден канал YouTube.");
    }

    public Task<YouTubeChannelIdentity?> ResolveChannelByHandleAsync(
        string handle,
        string accessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var trimmed = handle.Trim();
        var normalized = trimmed.StartsWith('@') ? trimmed : "@" + trimmed;
        return FetchFirstChannelAsync($"part=snippet&forHandle={Uri.EscapeDataString(normalized)}", accessToken, ct);
    }

    private async Task<YouTubeChannelIdentity?> FetchFirstChannelAsync(string query, string accessToken, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(YouTubeOAuthOptions.HttpClientName);
        var separator = options.ChannelsEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var requestUri = $"{options.ChannelsEndpoint}{separator}{query}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var reason = TryExtractErrorReason(content);

            logger.LogWarning(
                "YouTube Data API channels.list вернул {StatusCode} (reason={Reason})",
                (int)response.StatusCode,
                reason ?? "-");

            throw CreateApiException(response.StatusCode, reason);
        }

        var dto = JsonSerializer.Deserialize<YouTubeChannelsResponseDto>(content);
        var channel = dto?.Items?.FirstOrDefault();
        if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
        {
            return null;
        }

        return new(channel.Id.Trim(), channel.Snippet?.Title ?? channel.Id.Trim());
    }

    private static YouTubeOAuthException CreateApiException(HttpStatusCode statusCode, string? reason)
    {
        var message = reason switch
        {
            "accessNotConfigured" => "В Google Cloud Console для этого проекта не включён YouTube Data API v3 (accessNotConfigured). Включите API и повторите попытку.",
            "quotaExceeded" or "dailyLimitExceeded" or "rateLimitExceeded" => "Превышена квота YouTube Data API v3 для вашего проекта (quotaExceeded). Повторите вход после сброса квоты или проверьте лимиты в Google Cloud Console.",
            "youtubeSignupRequired" => "У выбранного Google-аккаунта нет созданного канала YouTube (youtubeSignupRequired). Создайте канал и повторите вход.",
            _ => string.IsNullOrWhiteSpace(reason)
                ? $"YouTube Data API вернул {(int)statusCode} при чтении канала."
                : $"YouTube Data API вернул {(int)statusCode} при чтении канала ({reason}).",
        };

        var isPermanentRejection = statusCode == HttpStatusCode.Unauthorized
            || reason is "accessNotConfigured" or "youtubeSignupRequired";

        return new(
            message,
            errorCode: reason,
            statusCode: statusCode,
            isPermanentRejection: isPermanentRejection);
    }

    private static string? TryExtractErrorReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<GoogleApiErrorEnvelopeDto>(body);
            return dto?.Error?.Errors?.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item.Reason))?.Reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class GoogleApiErrorEnvelopeDto
    {
        [JsonPropertyName("error")]
        public GoogleApiErrorBodyDto? Error { get; set; }
    }

    private sealed class GoogleApiErrorBodyDto
    {
        [JsonPropertyName("errors")]
        public List<GoogleApiErrorItemDto>? Errors { get; set; }
    }

    private sealed class GoogleApiErrorItemDto
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private sealed class YouTubeChannelsResponseDto
    {
        [JsonPropertyName("items")]
        public List<YouTubeChannelItemDto>? Items { get; set; }
    }

    private sealed class YouTubeChannelItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public YouTubeChannelSnippetDto? Snippet { get; set; }
    }

    private sealed class YouTubeChannelSnippetDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
