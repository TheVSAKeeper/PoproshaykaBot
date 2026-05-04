using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat;

public sealed class UserMessagesManagementService(
    StatisticsCollector statisticsCollector,
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

        var updated = statisticsCollector.DecrementUserMessages(userId, removedMessagesCount);

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

        var updated = statisticsCollector.IncrementUserMessages(userId, addedMessagesCount);

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
        return FormatMessage(messageSettings.PunishmentNotification, userName, removedMessagesCount);
    }

    public string GetRewardNotification(string userName, ulong addedMessagesCount)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        return FormatMessage(messageSettings.RewardNotification, userName, addedMessagesCount);
    }

    private static string FormatMessage(string template, string userName, ulong count)
    {
        return MessageTemplate.For(template)
            .With("username", userName)
            .With("count", count.ToString())
            .Render();
    }
}
