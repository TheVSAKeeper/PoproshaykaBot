using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Infrastructure.Events;

[SuppressMessage("Naming", "CA1711", Justification = "Обработчик события шины IEventBus, а не делегат .NET-события; имя – часть контракта всех подписчиков.")]
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
