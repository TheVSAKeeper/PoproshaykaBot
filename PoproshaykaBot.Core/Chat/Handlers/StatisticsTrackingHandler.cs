using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class StatisticsTrackingHandler : IEventHandler<ChatMessageReceived>, IEventSubscriber, IDisposable
{
    private readonly IUserStatisticsRepository _userStatistics;
    private readonly IBotStatisticsRepository _botStatistics;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly IDisposable _subscription;

    public StatisticsTrackingHandler(
        IUserStatisticsRepository userStatistics,
        IBotStatisticsRepository botStatistics,
        ITargetChannelProvider targetChannelProvider,
        IEventBus eventBus,
        ChannelCatalogStore? channelCatalogStore = null)
    {
        _userStatistics = userStatistics;
        _botStatistics = botStatistics;
        _targetChannelProvider = targetChannelProvider;
        _channelCatalogStore = channelCatalogStore;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.IsBot
            || @event.IsReplacement
            || @event.Origin is { IsCurrent: false }
            || !(@event.RecordsUserData ?? _targetChannelProvider.Current.RecordsUserData))
        {
            return Task.CompletedTask;
        }

        if (@event.ChannelKey is { } channelKey)
        {
            _userStatistics.TrackMessage(channelKey, @event.SenderKey, @event.Username);
            _channelCatalogStore?.EnsureChannelKnown(channelKey, @event.Channel);
        }
        else
        {
            _userStatistics.TrackMessage(@event.SenderKey, @event.Username);
        }

        _botStatistics.IncrementMessagesProcessed();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
