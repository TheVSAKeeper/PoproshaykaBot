using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events;

public static class EventBusPlatformExtensions
{
    public static IDisposable SubscribePlatform<TEvent>(
        this IEventBus bus,
        StreamingPlatform platform,
        IEventHandler<TEvent> handler)
        where TEvent : IPlatformEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        return bus.Subscribe<TEvent>((@event, cancellationToken) =>
            @event.Platform == platform
                ? handler.HandleAsync(@event, cancellationToken)
                : Task.CompletedTask);
    }

    public static IDisposable SubscribePlatform<TEvent>(
        this IEventBus bus,
        StreamingPlatform platform,
        Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IPlatformEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        return bus.Subscribe<TEvent>((@event, cancellationToken) =>
            @event.Platform == platform
                ? handler(@event, cancellationToken)
                : Task.CompletedTask);
    }

    public static IDisposable SubscribePlatform<TEvent>(
        this IEventBus bus,
        StreamingPlatform platform,
        Action<TEvent> handler)
        where TEvent : IPlatformEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        return bus.Subscribe<TEvent>(@event =>
        {
            if (@event.Platform == platform)
            {
                handler(@event);
            }
        });
    }
}
