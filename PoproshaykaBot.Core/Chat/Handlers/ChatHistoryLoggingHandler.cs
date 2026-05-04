using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ChatHistoryLoggingHandler : IEventHandler<ChatMessageReceived>, IEventSubscriber, IDisposable
{
    private readonly ChatHistoryManager _manager;
    private readonly IDisposable _subscription;

    public ChatHistoryLoggingHandler(ChatHistoryManager manager, IEventBus eventBus)
    {
        _manager = manager;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        _manager.AddMessage(@event.HistoryEntry);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
