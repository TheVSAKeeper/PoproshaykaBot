using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class StatisticsTrackingHandler : IEventHandler<ChatMessageReceived>, IEventSubscriber, IDisposable
{
    private readonly StatisticsCollector _collector;
    private readonly IDisposable _subscription;

    public StatisticsTrackingHandler(StatisticsCollector collector, IEventBus eventBus)
    {
        _collector = collector;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        _collector.TrackMessage(@event.UserId, @event.Username);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
