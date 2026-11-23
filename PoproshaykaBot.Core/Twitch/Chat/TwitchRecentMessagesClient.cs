using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using System.Text.Json;

namespace PoproshaykaBot.Core.Twitch.Chat;

public sealed class TwitchRecentMessagesClient(
    IHttpClientFactory httpClientFactory,
    ILogger<TwitchRecentMessagesClient> logger) : ITwitchRecentMessagesClient
{
    public const string HttpClientName = "TwitchRecentMessages";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private const string EndpointPrefix = "https://recent-messages.robotty.de/api/v2/recent-messages/";

    public async Task<IReadOnlyList<(ChatMessage Message, DateTimeOffset Timestamp)>> FetchAsync(
        string channelLogin,
        string channelId,
        string? botUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelLogin))
        {
            return [];
        }

        var normalizedLogin = channelLogin.Trim().TrimStart('#').ToLowerInvariant();
        var clampedLimit = Math.Clamp(limit, 1, MaxLimit);
        var url = $"{EndpointPrefix}{Uri.EscapeDataString(normalizedLogin)}?limit={clampedLimit}&hide_moderation_messages=true&hide_moderated_messages=true";

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "recent-messages.robotty.de вернул {StatusCode} для канала {Channel}",
                    (int)response.StatusCode,
                    normalizedLogin);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("messages", out var messagesEl)
                || messagesEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<(ChatMessage Message, DateTimeOffset Timestamp)>();
            foreach (var item in messagesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var rawLine = item.GetString();
                if (rawLine is not null
                    && TwitchIrcMessageParser.TryParsePrivMsg(rawLine, normalizedLogin, channelId, botUserId, out var msg, out var ts))
                {
                    parsed.Add((msg, ts));
                }
            }

            logger.LogInformation(
                "Загружено {Count} недавних сообщений Twitch для канала {Channel} через recent-messages.robotty.de",
                parsed.Count,
                normalizedLogin);

            return parsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Не удалось загрузить недавнюю историю Twitch для канала {Channel} через recent-messages.robotty.de",
                normalizedLogin);
            return [];
        }
    }
}
