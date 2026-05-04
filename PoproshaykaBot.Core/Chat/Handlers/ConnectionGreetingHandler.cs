using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ConnectionGreetingHandler : IEventHandler<BotJoinedChannel>, IEventSubscriber, IDisposable
{
    private readonly TwitchChatMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IDisposable _subscription;

    public ConnectionGreetingHandler(
        TwitchChatMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus)
    {
        _messenger = messenger;
        _settingsManager = settingsManager;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(BotJoinedChannel @event, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Current.Twitch;

        if (settings.Messages.ConnectionEnabled
            && !string.IsNullOrWhiteSpace(settings.Messages.Connection))
        {
            _messenger.Send(settings.Messages.Connection);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
