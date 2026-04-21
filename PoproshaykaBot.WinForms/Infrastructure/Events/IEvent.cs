namespace PoproshaykaBot.WinForms.Infrastructure.Events;

public interface IEvent;

public abstract record EventBase : IEvent
{
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}
