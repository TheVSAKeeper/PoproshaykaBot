using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Settings;

namespace PoproshaykaBot.WinForms.Server;

public sealed class SseChatSettingsSyncHandler : IEventHandler<ChatSettingsChangedEvent>, IEventSubscriber, IDisposable
{
    private readonly SseService _sseService;
    private readonly IDisposable _subscription;

    public SseChatSettingsSyncHandler(SseService sseService, IEventBus eventBus)
    {
        _sseService = sseService;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(ChatSettingsChangedEvent @event, CancellationToken cancellationToken)
    {
        _sseService.NotifyChatSettingsChanged(@event.Settings);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
