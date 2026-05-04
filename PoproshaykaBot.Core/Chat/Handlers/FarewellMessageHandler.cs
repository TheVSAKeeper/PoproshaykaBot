using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class FarewellMessageHandler : IEventHandler<BotLifecyclePhaseChanged>, IEventSubscriber, IDisposable
{
    private readonly TwitchChatHandler _twitchChatHandler;
    private readonly AudienceTracker _audienceTracker;
    private readonly TwitchChatMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IDisposable _subscription;

    public FarewellMessageHandler(
        TwitchChatHandler twitchChatHandler,
        AudienceTracker audienceTracker,
        TwitchChatMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus)
    {
        _twitchChatHandler = twitchChatHandler;
        _audienceTracker = audienceTracker;
        _messenger = messenger;
        _settingsManager = settingsManager;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(BotLifecyclePhaseChanged @event, CancellationToken cancellationToken)
    {
        if (@event.Phase != BotLifecyclePhase.Disconnecting)
        {
            return Task.CompletedTask;
        }

        var channel = _twitchChatHandler.Channel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return Task.CompletedTask;
        }

        var messages = new List<string>();
        var collectiveFarewell = _audienceTracker.CreateCollectiveFarewell();

        if (!string.IsNullOrWhiteSpace(collectiveFarewell))
        {
            messages.Add(collectiveFarewell);
        }

        var settings = _settingsManager.Current.Twitch;

        if (settings.Messages.DisconnectionEnabled
            && !string.IsNullOrWhiteSpace(settings.Messages.Disconnection))
        {
            messages.Add(settings.Messages.Disconnection);
        }

        if (messages.Count > 0)
        {
            try
            {
                _messenger.Send(string.Join(" ", messages));
            }
            catch (Exception)
            {
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
