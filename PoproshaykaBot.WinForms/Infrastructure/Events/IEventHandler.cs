namespace PoproshaykaBot.WinForms.Infrastructure.Events;

public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
