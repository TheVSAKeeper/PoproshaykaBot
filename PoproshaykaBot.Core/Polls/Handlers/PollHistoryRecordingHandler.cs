using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Polling;

namespace PoproshaykaBot.Core.Polls.Handlers;

public sealed class PollHistoryRecordingHandler :
    IEventHandler<PollFinalized>,
    IEventHandler<PollTerminated>,
    IEventHandler<PollArchived>,
    IEventSubscriber,
    IDisposable
{
    private readonly PollHistoryStore _store;
    private readonly List<IDisposable> _subscriptions;

    public PollHistoryRecordingHandler(PollHistoryStore store, IEventBus eventBus)
    {
        _store = store;
        _subscriptions =
        [
            eventBus.Subscribe<PollFinalized>(this),
            eventBus.Subscribe<PollTerminated>(this),
            eventBus.Subscribe<PollArchived>(this),
        ];
    }

    public Task HandleAsync(PollFinalized @event, CancellationToken cancellationToken)
    {
        _store.TryAdd(PollHistoryStore.BuildEntry(@event.Snapshot, @event.Winner, @event.WinnerIsTie));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollTerminated @event, CancellationToken cancellationToken)
    {
        var (winner, isTie) = PollEventSubMapper.DetectWinner(@event.Snapshot);
        _store.TryAdd(PollHistoryStore.BuildEntry(@event.Snapshot, winner, isTie));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollArchived @event, CancellationToken cancellationToken)
    {
        _store.TryAdd(PollHistoryStore.BuildEntry(@event.Snapshot, null, false));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
