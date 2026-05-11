using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ModerationChatNotificationHandler :
    IEventHandler<UserPunished>,
    IEventHandler<UserRewarded>,
    IEventSubscriber,
    IDisposable
{
    private readonly TwitchChatMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IDisposable _punishedSubscription;
    private readonly IDisposable _rewardedSubscription;

    public ModerationChatNotificationHandler(
        TwitchChatMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus)
    {
        _messenger = messenger;
        _settingsManager = settingsManager;
        _punishedSubscription = eventBus.Subscribe<UserPunished>(this);
        _rewardedSubscription = eventBus.Subscribe<UserRewarded>(this);
    }

    public Task HandleAsync(UserPunished @event, CancellationToken cancellationToken)
    {
        var messageSettings = _settingsManager.Current.Twitch.Messages;

        if (!messageSettings.PunishmentEnabled || string.IsNullOrWhiteSpace(@event.Channel))
        {
            return Task.CompletedTask;
        }

        var pointTerm = _settingsManager.Current.Ranks.PointTerm;
        var message = UserMessagesManagementService.FormatMessage(messageSettings.PunishmentMessage, @event.UserName, @event.RemovedMessagesCount, pointTerm);
        _messenger.Send(message);

        return Task.CompletedTask;
    }

    public Task HandleAsync(UserRewarded @event, CancellationToken cancellationToken)
    {
        var messageSettings = _settingsManager.Current.Twitch.Messages;

        if (!messageSettings.RewardEnabled || string.IsNullOrWhiteSpace(@event.Channel))
        {
            return Task.CompletedTask;
        }

        var pointTerm = _settingsManager.Current.Ranks.PointTerm;
        var message = UserMessagesManagementService.FormatMessage(messageSettings.RewardMessage, @event.UserName, @event.AddedMessagesCount, pointTerm);
        _messenger.Send(message);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _punishedSubscription.Dispose();
        _rewardedSubscription.Dispose();
    }
}
