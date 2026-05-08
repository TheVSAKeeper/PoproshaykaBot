using PoproshaykaBot.Core.Infrastructure.Events;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class BusControlExtensions
{
    public static IDisposable SubscribeOnUi<TEvent>(this IEventBus bus, Control host, Action<TEvent> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(handler);

        return bus.Subscribe<TEvent>(@event =>
        {
            if (host.IsDisposed || host.Disposing || !host.IsHandleCreated)
            {
                return;
            }

            try
            {
                if (host.InvokeRequired)
                {
                    host.BeginInvoke(() => InvokeGuarded(host, handler, @event));
                    return;
                }

                handler(@event);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException) when (host.IsDisposed)
            {
            }
        });
    }

    public static void DisposeOnClose(this List<IDisposable> subscriptions, Control host)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(host);

        host.Disposed += (_, _) =>
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }

            subscriptions.Clear();
        };
    }

    private static void InvokeGuarded<TEvent>(Control host, Action<TEvent> handler, TEvent @event)
    {
        if (host.IsDisposed || host.Disposing)
        {
            return;
        }

        try
        {
            handler(@event);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (host.IsDisposed)
        {
        }
    }
}
