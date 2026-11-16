using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Server;

public sealed class SseChatBridgeHandler :
    IEventHandler<ChatMessageReceived>,
    IEventHandler<ChatHistoryCleared>,
    IEventHandler<ChatHistoryLoaded>,
    IEventHandler<ChatMessagesDeleted>,
    IEventSubscriber,
    IDisposable
{
    private readonly SseService _sseService;
    private readonly IChatHistorySettingsProvider? _chatSettingsProvider;
    private readonly ObsChatStore? _obsChatStore;
    private readonly ChatHistoryManager? _historyManager;
    private readonly IDisposable _messageSubscription;
    private readonly IDisposable _clearSubscription;
    private readonly IDisposable _historyLoadedSubscription;
    private readonly IDisposable _messagesDeletedSubscription;

    public SseChatBridgeHandler(
        SseService sseService,
        IEventBus eventBus,
        IChatHistorySettingsProvider? chatSettingsProvider = null,
        ObsChatStore? obsChatStore = null,
        ChatHistoryManager? historyManager = null)
    {
        _sseService = sseService;
        _chatSettingsProvider = chatSettingsProvider;
        _obsChatStore = obsChatStore;
        _historyManager = historyManager;
        _messageSubscription = eventBus.Subscribe<ChatMessageReceived>(this);
        _clearSubscription = eventBus.Subscribe<ChatHistoryCleared>(this);
        _historyLoadedSubscription = eventBus.Subscribe<ChatHistoryLoaded>(this);
        _messagesDeletedSubscription = eventBus.Subscribe<ChatMessagesDeleted>(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.CommandResponse is { } commandResponse
            && !commandResponse.Target.GoesToOverlay())
        {
            return Task.CompletedTask;
        }

        _sseService.AddChatMessage(@event.HistoryEntry);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatHistoryCleared @event, CancellationToken cancellationToken)
    {
        _sseService.ClearChat(@event.ClearedAtUtc);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatHistoryLoaded @event, CancellationToken cancellationToken)
    {
        if (!_sseService.IsRunning)
        {
            return Task.CompletedTask;
        }

        if (_historyManager is not null && !_historyManager.IsActiveScope(@event.Scope))
        {
            return Task.CompletedTask;
        }

        var chatSettings = _chatSettingsProvider?.GetCurrent() ?? new ChatSettings();

        if (!chatSettings.ShowHistoryInObs)
        {
            return Task.CompletedTask;
        }

        var maxMessages = Math.Max(1, (_obsChatStore?.Load() ?? new ObsChatSettings()).MaxMessages);
        var entriesToSend = @event.AddedEntries
            .Where(entry => ObsOverlayMessageFilter.Allows(entry, chatSettings))
            .TakeLast(maxMessages);

        foreach (var entry in entriesToSend)
        {
            _sseService.AddChatMessage(entry);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatMessagesDeleted @event, CancellationToken cancellationToken)
    {
        _sseService.DeleteChatMessages(
            @event.DeletedMessageIds,
            @event.ModeratedUserId,
            @event.Platform.ToString(),
            @event.CutoffUtc,
            @event.Scope.ChannelId,
            @event.Scope.SessionScopeId,
            @event.AllowReplacement);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _messageSubscription.Dispose();
        _clearSubscription.Dispose();
        _historyLoadedSubscription.Dispose();
        _messagesDeletedSubscription.Dispose();
    }
}
