using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public sealed class UserMessagesManagementService(
    IUserStatisticsRepository userStatistics,
    SettingsManager settingsManager,
    IEventBus eventBus)
{
    public async Task<bool> PunishUserAsync(
        string userId,
        string userName,
        ulong removedMessagesCount,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        if (removedMessagesCount == 0)
        {
            return false;
        }

        var updated = userStatistics.IncrementShtrafMessages(userId, removedMessagesCount);

        if (!updated)
        {
            return false;
        }

        await eventBus.PublishAsync(new UserPunished(userId, userName, removedMessagesCount, channel), cancellationToken);

        return true;
    }

    public async Task<bool> RewardUserAsync(
        string userId,
        string userName,
        ulong addedMessagesCount,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        if (addedMessagesCount == 0)
        {
            return false;
        }

        var updated = userStatistics.IncrementBonusMessages(userId, addedMessagesCount);

        if (!updated)
        {
            return false;
        }

        await eventBus.PublishAsync(new UserRewarded(userId, userName, addedMessagesCount, channel), cancellationToken);

        return true;
    }

    public string GetPunishmentNotification(string userName, ulong removedMessagesCount)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        var pointTerm = settingsManager.Current.Ranks.PointTerm;
        return FormatMessage(messageSettings.PunishmentNotification, userName, removedMessagesCount, pointTerm);
    }

    public string GetRewardNotification(string userName, ulong addedMessagesCount)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        var pointTerm = settingsManager.Current.Ranks.PointTerm;
        return FormatMessage(messageSettings.RewardNotification, userName, addedMessagesCount, pointTerm);
    }

    internal static string FormatMessage(string template, string userName, ulong count, PointTerm pointTerm)
    {
        var signed = (long)count;
        return MessageTemplate.For(template)
            .With("username", userName)
            .With("count", count.ToString())
            .With("points", $"{FormattingUtils.FormatNumber(count)} {pointTerm.ForCount(signed)}")
            .Render();
    }
}
