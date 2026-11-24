using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class YouTubeRestLiveChatSendClient(
    IHttpClientFactory httpClientFactory,
    IYouTubeQuotaGovernor quotaGovernor)
    : IYouTubeLiveChatSendClient
{
    public const int InsertQuotaCost = YouTubeQuotaCosts.ChatSendUnits;

    private const string InsertUri = "youtube/v3/liveChat/messages?part=snippet";
    private const string TextMessageEventType = "textMessageEvent";
    private const string OperationName = "liveChatMessages.insert";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<YouTubeInsertMessageResult> InsertMessageAsync(
        string liveChatId,
        string messageText,
        YouTubeQuotaPriority priority,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveChatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);

        if (!quotaGovernor.TryReserve(PlatformAuthRole.Bot, priority, InsertQuotaCost, out var lease, YouTubeQuotaCategory.ChatSend))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.LocalQuotaBudgetReached,
                "Локальный бюджет квоты YouTube исчерпан перед liveChatMessages.insert.",
                reason: "localBudgetReached");
        }

        using (lease)
        using (var client = httpClientFactory.CreateClient(YouTubeEndpoints.BotApiHttpClient))
        {
            var payload = new LiveChatMessageInsertRequestDto(
                new(liveChatId, TextMessageEventType, new(messageText)));
            var requestJson = JsonSerializer.Serialize(payload, JsonOptions);

            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await client.PostAsync(InsertUri, content, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lease.Commit();
                throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lease.Commit();
                throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
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
                    lease.Commit();
                    throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lease.Commit();
                    throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var apiException = YouTubeErrorClassifier.FromHttpResponse(response.StatusCode, body, OperationName);

                    if (apiException.Kind == YouTubeApiErrorKind.QuotaExceeded)
                    {
                        quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Bot);
                    }
                    else if (response.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        lease.Commit();
                    }

                    throw apiException;
                }

                lease.Commit();
                return ParseSuccessResponse(body);
            }
        }
    }

    private static YouTubeInsertMessageResult ParseSuccessResponse(string json)
    {
        LiveChatMessageInsertResponseDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<LiveChatMessageInsertResponseDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Transient,
                "YouTube Data API (liveChatMessages.insert) вернул ответ без идентификатора сообщения.");
        }

        return new(dto.Id, dto.Snippet?.PublishedAt);
    }

    private sealed record LiveChatMessageInsertRequestDto(
        [property: JsonPropertyName("snippet")] LiveChatMessageInsertSnippetDto Snippet);

    private sealed record LiveChatMessageInsertSnippetDto(
        [property: JsonPropertyName("liveChatId")] string LiveChatId,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("textMessageDetails")] LiveChatTextMessageDetailsDto TextMessageDetails);

    private sealed record LiveChatTextMessageDetailsDto(
        [property: JsonPropertyName("messageText")] string MessageText);

    private sealed class LiveChatMessageInsertResponseDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveChatMessageInsertResponseSnippetDto? Snippet { get; set; }
    }

    private sealed class LiveChatMessageInsertResponseSnippetDto
    {
        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
