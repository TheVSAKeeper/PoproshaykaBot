using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.WinForms.Infrastructure.Events;

public sealed class InMemoryEventBus(ILogger<InMemoryEventBus> logger) : IEventBus
{
    private readonly Dictionary<Type, List<HandlerRegistration>> _handlers = new();
    private readonly object _syncLock = new();

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        HandlerRegistration[] snapshot;

        lock (_syncLock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var registrations) || registrations.Count == 0)
            {
                logger.LogTrace("Нет подписчиков для события {EventType}", typeof(TEvent).Name);
                return;
            }

            snapshot = registrations.ToArray();
        }

        logger.LogDebug("Публикация события {EventType} для {HandlerCount} подписчиков", typeof(TEvent).Name, snapshot.Length);

        foreach (var registration in snapshot)
        {
            try
            {
                await registration.Invoker(@event!, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Обработчик события {EventType} завершился с ошибкой",
                    typeof(TEvent).Name);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore<TEvent>(handler, (e, ct) => handler.HandleAsync((TEvent)e, ct));
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore<TEvent>(handler, (e, ct) => handler((TEvent)e, ct));
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore<TEvent>(handler, (e, _) =>
        {
            handler((TEvent)e);
            return Task.CompletedTask;
        });
    }

    private Subscription SubscribeCore<TEvent>(object key, Func<object, CancellationToken, Task> invoker)
        where TEvent : IEvent
    {
        var registration = new HandlerRegistration(key, invoker);

        lock (_syncLock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var registrations))
            {
                registrations = [];
                _handlers[typeof(TEvent)] = registrations;
            }

            registrations.Add(registration);
        }

        logger.LogDebug("Добавлена подписка на событие {EventType}", typeof(TEvent).Name);

        return new(() =>
        {
            lock (_syncLock)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var registrations))
                {
                    registrations.Remove(registration);
                }
            }

            logger.LogDebug("Удалена подписка на событие {EventType}", typeof(TEvent).Name);
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed record HandlerRegistration(object Key, Func<object, CancellationToken, Task> Invoker);
}
