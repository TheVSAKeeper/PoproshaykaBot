using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public sealed class UserPointsManagementService(
    IUserStatisticsRepository userStatistics,
    SettingsManager settingsManager,
    IEventBus eventBus)
{
    public async Task<bool> PunishUserAsync(
        string userId,
        string userName,
        ulong removedPoints,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        if (removedPoints == 0)
        {
            return false;
        }

        var updated = userStatistics.IncrementPenaltyPoints(userId, removedPoints);

        if (!updated)
        {
            return false;
        }

        await eventBus.PublishAsync(new UserPunished(userId, userName, removedPoints, channel), cancellationToken);

        return true;
    }

    public async Task<bool> RewardUserAsync(
        string userId,
        string userName,
        ulong addedPoints,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        if (addedPoints == 0)
        {
            return false;
        }

        var updated = userStatistics.IncrementBonusPoints(userId, addedPoints);

        if (!updated)
        {
            return false;
        }

        await eventBus.PublishAsync(new UserRewarded(userId, userName, addedPoints, channel), cancellationToken);

        return true;
    }

    public string GetPunishmentNotification(string userName, ulong removedPoints)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        var pointTerm = settingsManager.Current.Ranks.PointTerm;
        return FormatMessage(messageSettings.PunishmentNotification, userName, removedPoints, pointTerm);
    }

    public string GetRewardNotification(string userName, ulong addedPoints)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        var pointTerm = settingsManager.Current.Ranks.PointTerm;
        return FormatMessage(messageSettings.RewardNotification, userName, addedPoints, pointTerm);
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
