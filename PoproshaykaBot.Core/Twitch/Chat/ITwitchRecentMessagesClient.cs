using PoproshaykaBot.Core.Chat;

namespace PoproshaykaBot.Core.Twitch.Chat;

public interface ITwitchRecentMessagesClient
{
    Task<IReadOnlyList<(ChatMessage Message, DateTimeOffset Timestamp)>> FetchAsync(
        string channelLogin,
        string channelId,
        string? botUserId,
        int limit,
        CancellationToken cancellationToken);
}
