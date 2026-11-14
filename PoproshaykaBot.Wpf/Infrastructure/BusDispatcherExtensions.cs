using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class BusDispatcherExtensions
{
    private static Serilog.ILogger HostLog => Log.ForContext(typeof(BusDispatcherExtensions));

    public static IDisposable SubscribeOnUi<TEvent>(this IEventBus bus, Action<TEvent> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new UiSubscription();
        subscription.Attach(bus.Subscribe<TEvent>(@event => DispatchToUi(subscription, handler, @event)));

        return subscription;
    }

    public static IDisposable SubscribePlatformOnUi<TEvent>(
        this IEventBus bus,
        StreamingPlatform platform,
        Action<TEvent> handler)
        where TEvent : IPlatformEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new UiSubscription();
        subscription.Attach(bus.SubscribePlatform<TEvent>(platform, @event => DispatchToUi(subscription, handler, @event)));

        return subscription;
    }

    private static void DispatchToUi<TEvent>(UiSubscription subscription, Action<TEvent> handler, TEvent @event)
    {
        if (subscription.IsDisposed)
        {
            return;
        }

        // В тестовом проекте TestResources создаёт голый Application без цикла Dispatcher.Run
        // на потоке первой STA-фикстуры; маршалинг туда из других фикстур теряет события.
        var dispatcher = Application.Current is App app ? app.Dispatcher : null;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            handler(@event);
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(() => InvokeGuarded(subscription, dispatcher, handler, @event));
        }
        catch (InvalidOperationException exception)
        {
            HostLog.Debug(exception, "Событие {Event} не поставлено в очередь: диспетчер уже завершён", typeof(TEvent).Name);
        }
    }

    private static void InvokeGuarded<TEvent>(UiSubscription subscription, Dispatcher dispatcher, Action<TEvent> handler, TEvent @event)
    {
        if (subscription.IsDisposed || dispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            handler(@event);
        }
        catch (ObjectDisposedException exception)
        {
            HostLog.Debug(exception, "Событие {Event} доставлено освобождённому получателю", typeof(TEvent).Name);
        }
    }

    private sealed class UiSubscription : IDisposable
    {
        private IDisposable? _inner;
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public void Attach(IDisposable inner)
        {
            Interlocked.Exchange(ref _inner, inner);

            if (IsDisposed)
            {
                DisposeInner();
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            DisposeInner();
        }

        private void DisposeInner()
        {
            Interlocked.Exchange(ref _inner, null)?.Dispose();
        }
    }
}
