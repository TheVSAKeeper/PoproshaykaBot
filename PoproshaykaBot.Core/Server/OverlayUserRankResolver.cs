using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Server;

public sealed class OverlayUserRankResolver(
    IUserStatisticsRepository? statistics = null,
    UserRankService? rankService = null)
{
    public UserRank? Resolve(ChatMessageData chatMessage)
    {
        ArgumentNullException.ThrowIfNull(chatMessage);

        if (statistics is null || rankService is null)
        {
            return null;
        }

        if (chatMessage.MessageType != ChatMessageType.UserMessage
            || chatMessage.IsBot
            || string.IsNullOrWhiteSpace(chatMessage.UserId))
        {
            return null;
        }

        var userKey = new PlatformUserKey(chatMessage.Platform, chatMessage.UserId);
        var viewer = statistics.FindViewerById(StatisticsSlice.All(), userKey);
        return rankService.GetRank(viewer?.Points ?? 0);
    }
}
