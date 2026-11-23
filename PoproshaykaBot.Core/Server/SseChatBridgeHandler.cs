using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Server;

public sealed class SseChatBridgeHandler :
    IEventHandler<ChatMessageReceived>,
    IEventHandler<ChatHistoryCleared>,
    IEventHandler<ChatHistoryLoaded>,
    IEventHandler<ChatMessagesDeleted>,
    IEventHandler<ActiveChannelChanged>,
    IEventHandler<YouTubeActiveBroadcastChanged>,
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventSubscriber,
    IDisposable
{
    private readonly SseService _sseService;
    private readonly IChatHistorySettingsProvider? _chatSettingsProvider;
    private readonly ObsChatStore? _obsChatStore;
    private readonly ChatHistoryManager? _historyManager;
    private readonly TimeProvider _timeProvider;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IYouTubeActiveBroadcastSource? _youtubeBroadcastSource;
    private readonly IBroadcastState? _broadcastState;
    private readonly IDisposable _messageSubscription;
    private readonly IDisposable _clearSubscription;
    private readonly IDisposable _historyLoadedSubscription;
    private readonly IDisposable _messagesDeletedSubscription;
    private readonly IDisposable _activeChannelSubscription;
    private readonly IDisposable _youtubeBroadcastSubscription;
    private readonly IDisposable _streamOnlineSubscription;
    private readonly IDisposable _streamOfflineSubscription;

    public SseChatBridgeHandler(
        SseService sseService,
        IEventBus eventBus,
        IChatHistorySettingsProvider? chatSettingsProvider = null,
        ObsChatStore? obsChatStore = null,
        ChatHistoryManager? historyManager = null,
        TimeProvider? timeProvider = null,
        IChatPlatformRegistry? platformRegistry = null,
        IYouTubeActiveBroadcastSource? youtubeBroadcastSource = null,
        IBroadcastState? broadcastState = null)
    {
        _sseService = sseService;
        _chatSettingsProvider = chatSettingsProvider;
        _obsChatStore = obsChatStore;
        _historyManager = historyManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _platformRegistry = platformRegistry;
        _youtubeBroadcastSource = youtubeBroadcastSource;
        _broadcastState = broadcastState;
        _messageSubscription = eventBus.Subscribe<ChatMessageReceived>(this);
        _clearSubscription = eventBus.Subscribe<ChatHistoryCleared>(this);
        _historyLoadedSubscription = eventBus.Subscribe<ChatHistoryLoaded>(this);
        _messagesDeletedSubscription = eventBus.Subscribe<ChatMessagesDeleted>(this);
        _activeChannelSubscription = eventBus.Subscribe<ActiveChannelChanged>(this);
        _youtubeBroadcastSubscription = eventBus.SubscribePlatform<YouTubeActiveBroadcastChanged>(StreamingPlatform.YouTube, this);
        _streamOnlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _streamOfflineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.CommandResponse is { } commandResponse
            && !commandResponse.Target.GoesToOverlay())
        {
            return Task.CompletedTask;
        }

        var incomingScope = ChatHistoryScope.FromMessage(@event.HistoryEntry, @event.Channel);
        if (_historyManager is not null
            && !_historyManager.IsActiveChannel(incomingScope.Platform, incomingScope.ChannelId))
        {
            return Task.CompletedTask;
        }

        if (!ObsOverlayMessageFilter.AllowsCurrentStream(
                @event.HistoryEntry,
                _platformRegistry,
                _youtubeBroadcastSource,
                _broadcastState))
        {
            return Task.CompletedTask;
        }

        _sseService.AddChatMessage(@event.HistoryEntry);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ActiveChannelChanged @event, CancellationToken cancellationToken)
    {
        ReloadOverlayClients();
        return Task.CompletedTask;
    }

    public Task HandleAsync(YouTubeActiveBroadcastChanged @event, CancellationToken cancellationToken)
    {
        ReloadOverlayClients();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        ReloadOverlayClients();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        ReloadOverlayClients();
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
            .Where(entry => ObsOverlayMessageFilter.Allows(entry, chatSettings)
                && ObsOverlayMessageFilter.AllowsCurrentStream(entry, _platformRegistry, _youtubeBroadcastSource, _broadcastState))
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
        _activeChannelSubscription.Dispose();
        _youtubeBroadcastSubscription.Dispose();
        _streamOnlineSubscription.Dispose();
        _streamOfflineSubscription.Dispose();
    }

    private void ReloadOverlayClients()
    {
        if (!_sseService.IsRunning)
        {
            return;
        }

        _sseService.ClearChat(reloadHistory: true);

        if (!_sseService.HasLegacyClients || _historyManager is null)
        {
            return;
        }

        var chatSettings = _chatSettingsProvider?.GetCurrent() ?? new ChatSettings();
        var obsSettings = _obsChatStore?.Load() ?? new ObsChatSettings();
        var maxMessages = Math.Max(1, obsSettings.MaxMessages);
        var entries = _historyManager
            .GetActiveHistory()
            .Where(entry => ObsOverlayMessageFilter.Allows(entry, chatSettings)
                && ObsOverlayMessageFilter.AllowsCurrentStream(entry, _platformRegistry, _youtubeBroadcastSource, _broadcastState));

        if (obsSettings.EnableMessageFadeOut)
        {
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-obsSettings.MessageLifetimeSeconds);
            entries = entries.Where(entry => entry.IsHistorical || entry.Timestamp >= cutoff);
        }

        foreach (var entry in entries.TakeLast(maxMessages))
        {
            _sseService.AddChatMessage(entry, legacyClientsOnly: true);
        }
    }
}
