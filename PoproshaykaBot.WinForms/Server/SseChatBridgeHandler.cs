using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Chat;

namespace PoproshaykaBot.WinForms.Server;

public sealed class SseChatBridgeHandler :
    IEventHandler<ChatMessageReceived>,
    IEventHandler<ChatHistoryCleared>,
    IEventSubscriber,
    IDisposable
{
    private readonly SseService _sseService;
    private readonly IDisposable _messageSubscription;
    private readonly IDisposable _clearSubscription;

    public SseChatBridgeHandler(SseService sseService, IEventBus eventBus)
    {
        _sseService = sseService;
        _messageSubscription = eventBus.Subscribe<ChatMessageReceived>(this);
        _clearSubscription = eventBus.Subscribe<ChatHistoryCleared>(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        _sseService.AddChatMessage(@event.HistoryEntry);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatHistoryCleared @event, CancellationToken cancellationToken)
    {
        _sseService.ClearChat();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _messageSubscription.Dispose();
        _clearSubscription.Dispose();
    }
}
