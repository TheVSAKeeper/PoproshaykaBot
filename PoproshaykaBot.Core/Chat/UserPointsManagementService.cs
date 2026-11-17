using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public sealed class UserPointsManagementService(
    IUserStatisticsRepository userStatistics,
    SettingsManager settingsManager,
    IEventBus eventBus,
    ILogger<UserPointsManagementService> logger)
{
    private const string UnknownSource = "не указан";

    public Task<bool> PunishUserAsync(
        PlatformUserKey userKey,
        string userName,
        ulong removedPoints,
        string? channel,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return AdjustPointsAsync(
            target: null,
            userKey,
            userName,
            removedPoints,
            channel,
            source,
            isBonus: false,
            cancellationToken);
    }

    public Task<bool> PunishUserAsync(
        StatisticsCounterTarget target,
        PlatformUserKey userKey,
        string userName,
        ulong removedPoints,
        string? channel,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return AdjustPointsAsync(
            target,
            userKey,
            userName,
            removedPoints,
            channel,
            source,
            isBonus: false,
            cancellationToken);
    }

    public Task<bool> RewardUserAsync(
        PlatformUserKey userKey,
        string userName,
        ulong addedPoints,
        string? channel,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return AdjustPointsAsync(
            target: null,
            userKey,
            userName,
            addedPoints,
            channel,
            source,
            isBonus: true,
            cancellationToken);
    }

    public Task<bool> RewardUserAsync(
        StatisticsCounterTarget target,
        PlatformUserKey userKey,
        string userName,
        ulong addedPoints,
        string? channel,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return AdjustPointsAsync(
            target,
            userKey,
            userName,
            addedPoints,
            channel,
            source,
            isBonus: true,
            cancellationToken);
    }

    private async Task<bool> AdjustPointsAsync(
        StatisticsCounterTarget? target,
        PlatformUserKey userKey,
        string userName,
        ulong delta,
        string? channel,
        string? source,
        bool isBonus,
        CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return false;
        }

        var updated = target is { } explicitTarget
            ? isBonus
                ? userStatistics.IncrementBonusPoints(explicitTarget, userKey, delta, userName)
                : userStatistics.IncrementPenaltyPoints(explicitTarget, userKey, delta, userName)
            : isBonus
                ? userStatistics.IncrementBonusPoints(userKey, delta)
                : userStatistics.IncrementPenaltyPoints(userKey, delta);

        if (!updated)
        {
            return false;
        }

        if (isBonus)
        {
            logger.LogInformation(
                "Пользователю {UserName} ({Platform} {UserId}) начислено {Delta} бонусных баллов, источник: {Source}",
                userName, userKey.Platform, userKey.UserId, delta, source ?? UnknownSource);
            await eventBus.PublishAsync(
                new UserRewarded(userKey, userName, delta, channel, target?.AsChannelKey),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation(
                "Пользователю {UserName} ({Platform} {UserId}) начислено {Delta} штрафных баллов, источник: {Source}",
                userName, userKey.Platform, userKey.UserId, delta, source ?? UnknownSource);
            await eventBus.PublishAsync(
                new UserPunished(userKey, userName, delta, channel, target?.AsChannelKey),
                cancellationToken).ConfigureAwait(false);
        }

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
