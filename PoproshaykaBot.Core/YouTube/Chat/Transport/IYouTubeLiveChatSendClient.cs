using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal readonly record struct YouTubeInsertMessageResult(
    string MessageId,
    DateTimeOffset? PublishedAtUtc);

internal interface IYouTubeLiveChatSendClient
{
    Task<YouTubeInsertMessageResult> InsertMessageAsync(
        string liveChatId,
        string messageText,
        YouTubeQuotaPriority priority,
        CancellationToken cancellationToken);
}
