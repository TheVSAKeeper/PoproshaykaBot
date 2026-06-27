using PoproshaykaBot.Core.Infrastructure.Events;
using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class BusDispatcherExtensions
{
    public static IDisposable SubscribeOnUi<TEvent>(this IEventBus bus, Action<TEvent> handler)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(handler);

        return bus.Subscribe<TEvent>(@event =>
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.CheckAccess())
            {
                handler(@event);
                return;
            }

            dispatcher.BeginInvoke(() => handler(@event));
        });
    }
}
