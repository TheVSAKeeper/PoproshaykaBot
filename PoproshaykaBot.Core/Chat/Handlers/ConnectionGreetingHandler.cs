using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ConnectionGreetingHandler : IEventHandler<BotJoinedChannel>, IEventSubscriber, IDisposable
{
    private readonly IBroadcastMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IDisposable _subscription;

    public ConnectionGreetingHandler(
        IBroadcastMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus)
    {
        _messenger = messenger;
        _settingsManager = settingsManager;
        _subscription = eventBus.Subscribe<BotJoinedChannel>(this);
    }

    public Task HandleAsync(BotJoinedChannel @event, CancellationToken cancellationToken)
    {
        var messages = _settingsManager.Current.ChatMessages;

        if (messages.ConnectionEnabled
            && !string.IsNullOrWhiteSpace(messages.Connection))
        {
            _messenger.Send(@event.Platform, messages.Connection, ChatSendCategory.Greeting);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
