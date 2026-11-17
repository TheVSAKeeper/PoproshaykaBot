using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ModerationChatNotificationHandler :
    IEventHandler<UserPunished>,
    IEventHandler<UserRewarded>,
    IEventSubscriber,
    IDisposable
{
    private readonly IBroadcastMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ChannelCatalogStore? _channelCatalog;
    private readonly IDisposable _punishedSubscription;
    private readonly IDisposable _rewardedSubscription;

    public ModerationChatNotificationHandler(
        IBroadcastMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus,
        IChatPlatformRegistry? platformRegistry = null,
        ChannelCatalogStore? channelCatalog = null)
    {
        _messenger = messenger;
        _settingsManager = settingsManager;
        _platformRegistry = platformRegistry;
        _channelCatalog = channelCatalog;
        _punishedSubscription = eventBus.Subscribe<UserPunished>(this);
        _rewardedSubscription = eventBus.Subscribe<UserRewarded>(this);
    }

    public Task HandleAsync(UserPunished @event, CancellationToken cancellationToken)
    {
        var messageSettings = _settingsManager.Current.ChatMessages;

        if (!messageSettings.PunishmentEnabled
            || string.IsNullOrWhiteSpace(@event.Channel)
            || !ShouldSendNotification(@event.Platform, @event.ChannelKey, @event.Channel))
        {
            return Task.CompletedTask;
        }

        var pointTerm = _settingsManager.Current.Ranks.PointTerm;
        var message = UserPointsManagementService.FormatMessage(messageSettings.PunishmentMessage, @event.UserName, @event.RemovedPoints, pointTerm);
        _messenger.Send(@event.Platform, message, ChatSendCategory.EventNotification);

        return Task.CompletedTask;
    }

    public Task HandleAsync(UserRewarded @event, CancellationToken cancellationToken)
    {
        var messageSettings = _settingsManager.Current.ChatMessages;

        if (!messageSettings.RewardEnabled
            || string.IsNullOrWhiteSpace(@event.Channel)
            || !ShouldSendNotification(@event.Platform, @event.ChannelKey, @event.Channel))
        {
            return Task.CompletedTask;
        }

        var pointTerm = _settingsManager.Current.Ranks.PointTerm;
        var message = UserPointsManagementService.FormatMessage(messageSettings.RewardMessage, @event.UserName, @event.AddedPoints, pointTerm);
        _messenger.Send(@event.Platform, message, ChatSendCategory.EventNotification);

        return Task.CompletedTask;
    }

    private bool ShouldSendNotification(StreamingPlatform platform, ChannelKey? targetChannelKey, string? eventChannel)
    {
        if (targetChannelKey is not { } targetKey)
        {
            return true;
        }

        if (_platformRegistry is null)
        {
            return true;
        }

        var activeChannel = _platformRegistry.GetConfiguredChannel(platform)?.Trim();
        if (string.IsNullOrWhiteSpace(activeChannel))
        {
            return false;
        }

        if (string.Equals(targetKey.ChannelId, activeChannel, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(eventChannel)
                && string.Equals(eventChannel.Trim(), activeChannel, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (_channelCatalog is null)
        {
            return false;
        }

        var entries = _channelCatalog.Load().Channels;
        foreach (var entry in entries)
        {
            if (entry.Platform != targetKey.Platform)
            {
                continue;
            }

            var matchesActive = string.Equals(entry.ChannelId, activeChannel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.DisplayName, activeChannel, StringComparison.OrdinalIgnoreCase)
                || entry.KnownAliases.Any(alias => string.Equals(alias, activeChannel, StringComparison.OrdinalIgnoreCase));

            if (string.Equals(entry.ChannelId, targetKey.ChannelId, StringComparison.Ordinal))
            {
                return matchesActive;
            }

            if (matchesActive)
            {
                return false;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _punishedSubscription.Dispose();
        _rewardedSubscription.Dispose();
    }
}
