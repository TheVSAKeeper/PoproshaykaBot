using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms;

public sealed class UserMessagesManagementService(
    StatisticsCollector statisticsCollector,
    TwitchChatMessenger messenger,
    SettingsManager settingsManager)
{
    public bool PunishUser(string userId, string userName, ulong removedMessagesCount, string? channel)
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

        var messageSettings = settingsManager.Current.Twitch.Messages;
        if (messageSettings.PunishmentEnabled && !string.IsNullOrWhiteSpace(channel))
        {
            var message = messageSettings.PunishmentMessage
                .Replace("{username}", userName)
                .Replace("{count}", removedMessagesCount.ToString());

            messenger.Send(channel, message);
        }

        return true;
    }

    public bool RewardUser(string userId, string userName, ulong addedMessagesCount, string? channel)
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

        var messageSettings = settingsManager.Current.Twitch.Messages;
        if (messageSettings.RewardEnabled && !string.IsNullOrWhiteSpace(channel))
        {
            var message = messageSettings.RewardMessage
                .Replace("{username}", userName)
                .Replace("{count}", addedMessagesCount.ToString());

            messenger.Send(channel, message);
        }

        return true;
    }

    public string GetPunishmentNotification(string userName, ulong removedMessagesCount)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        return messageSettings.PunishmentNotification
            .Replace("{username}", userName)
            .Replace("{count}", removedMessagesCount.ToString());
    }

    public string GetRewardNotification(string userName, ulong addedMessagesCount)
    {
        var messageSettings = settingsManager.Current.Twitch.Messages;
        return messageSettings.RewardNotification
            .Replace("{username}", userName)
            .Replace("{count}", addedMessagesCount.ToString());
    }
}
