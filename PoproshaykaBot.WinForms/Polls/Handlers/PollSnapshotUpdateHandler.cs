using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

namespace PoproshaykaBot.WinForms.Polls.Handlers;

public sealed class PollSnapshotUpdateHandler :
    IEventHandler<PollStarted>,
    IEventHandler<PollProgressed>,
    IEventHandler<PollFinalized>,
    IEventHandler<PollTerminated>,
    IEventHandler<PollArchived>,
    IEventSubscriber,
    IDisposable
{
    private readonly PollSnapshotStore _store;
    private readonly List<IDisposable> _subscriptions;

    public PollSnapshotUpdateHandler(PollSnapshotStore store, IEventBus eventBus)
    {
        _store = store;
        _subscriptions =
        [
            eventBus.Subscribe<PollStarted>(this),
            eventBus.Subscribe<PollProgressed>(this),
            eventBus.Subscribe<PollFinalized>(this),
            eventBus.Subscribe<PollTerminated>(this),
            eventBus.Subscribe<PollArchived>(this),
        ];
    }

    public Task HandleAsync(PollStarted @event, CancellationToken cancellationToken)
    {
        _store.Set(Merge(@event.Snapshot));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollProgressed @event, CancellationToken cancellationToken)
    {
        _store.Set(Merge(@event.Snapshot));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollFinalized @event, CancellationToken cancellationToken)
    {
        _store.Set(Merge(@event.Snapshot));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollTerminated @event, CancellationToken cancellationToken)
    {
        _store.Set(Merge(@event.Snapshot));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollArchived @event, CancellationToken cancellationToken)
    {
        _store.Set(Merge(@event.Snapshot));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private PollSnapshot Merge(PollSnapshot incoming)
    {
        var current = _store.Current;

        if (current is not null
            && string.Equals(current.PollId, incoming.PollId, StringComparison.Ordinal)
            && current.SourceProfileId is not null
            && incoming.SourceProfileId is null)
        {
            return incoming with { SourceProfileId = current.SourceProfileId };
        }

        return incoming;
    }
}
