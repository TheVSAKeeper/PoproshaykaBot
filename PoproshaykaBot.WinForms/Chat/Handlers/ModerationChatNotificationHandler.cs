using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Moderation;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Chat.Handlers;

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

        var message = FormatMessage(messageSettings.PunishmentMessage, @event.UserName, @event.RemovedMessagesCount);
        _messenger.Send(@event.Channel, message);

        return Task.CompletedTask;
    }

    public Task HandleAsync(UserRewarded @event, CancellationToken cancellationToken)
    {
        var messageSettings = _settingsManager.Current.Twitch.Messages;

        if (!messageSettings.RewardEnabled || string.IsNullOrWhiteSpace(@event.Channel))
        {
            return Task.CompletedTask;
        }

        var message = FormatMessage(messageSettings.RewardMessage, @event.UserName, @event.AddedMessagesCount);
        _messenger.Send(@event.Channel, message);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _punishedSubscription.Dispose();
        _rewardedSubscription.Dispose();
    }

    private static string FormatMessage(string template, string userName, ulong count)
    {
        return MessageTemplate.For(template)
            .With("username", userName)
            .With("count", count.ToString())
            .Render();
    }
}
