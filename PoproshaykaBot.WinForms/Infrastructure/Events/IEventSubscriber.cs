namespace PoproshaykaBot.WinForms.Infrastructure.Events;

/// <summary>
/// Marker for classes that subscribe to <see cref="IEventBus" /> in their constructor.
/// Auto-registered as singleton and eagerly resolved at startup so subscriptions take effect.
/// </summary>
public interface IEventSubscriber;
