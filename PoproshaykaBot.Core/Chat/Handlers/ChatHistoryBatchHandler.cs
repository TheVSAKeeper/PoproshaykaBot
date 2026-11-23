using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ChatHistoryBatchHandler : IEventHandler<RawChatHistoryReceived>, IEventSubscriber, IDisposable
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly ObsChatStore _obsChatStore;
    private readonly ChatHistoryManager _historyManager;
    private readonly IEventBus _eventBus;
    private readonly CommandResponseTracker? _commandResponseTracker;
    private readonly IBroadcastState? _broadcastState;
    private readonly IDisposable _subscription;

    public ChatHistoryBatchHandler(
        IChatPlatformRegistry platformRegistry,
        ObsChatStore obsChatStore,
        ChatHistoryManager historyManager,
        IEventBus eventBus,
        CommandResponseTracker? commandResponseTracker = null,
        IBroadcastState? broadcastState = null)
    {
        ArgumentNullException.ThrowIfNull(platformRegistry);
        ArgumentNullException.ThrowIfNull(obsChatStore);
        ArgumentNullException.ThrowIfNull(historyManager);
        ArgumentNullException.ThrowIfNull(eventBus);

        _platformRegistry = platformRegistry;
        _obsChatStore = obsChatStore;
        _historyManager = historyManager;
        _eventBus = eventBus;
        _commandResponseTracker = commandResponseTracker;
        _broadcastState = broadcastState;
        _subscription = eventBus.Subscribe(this);
    }

    public async Task HandleAsync(RawChatHistoryReceived @event, CancellationToken cancellationToken)
    {
        if (!_platformRegistry.TryGet(@event.Platform, out var platform)
            || (!@event.AllowExternalHistory
                && (platform.Capabilities & PlatformCapabilities.ServerChatHistory) == PlatformCapabilities.None))
        {
            return;
        }

        var obsChat = _obsChatStore.Load();
        var liveStream = platform.StreamStatus is { CurrentStatus: StreamStatus.Online } streamStatus
            ? streamStatus.CurrentStream
            : null;
        var currentBroadcastId = liveStream is not null ? _broadcastState?.CurrentBroadcastId : null;
        var liveStartUtc = liveStream is { StartedAt: var startedAt } && startedAt != default
            ? DateTime.SpecifyKind(startedAt, DateTimeKind.Utc)
            : (DateTime?)null;
        var scopeChannelKey = !string.IsNullOrWhiteSpace(@event.Scope.ChannelId)
            ? new ChannelKey(@event.Platform, @event.Scope.ChannelId)
            : (ChannelKey?)null;
        var entries = new List<ChatMessageData>(@event.Messages.Count);

        foreach (var (message, timestamp) in @event.Messages)
        {
            var messageChannelKey = scopeChannelKey
                ?? (!string.IsNullOrWhiteSpace(message.ChannelId)
                    ? new ChannelKey(message.Platform, message.ChannelId)
                    : (ChannelKey?)null);
            var commandResponse = message.IsBot
                ? (_commandResponseTracker?.TryConsume(message.Platform, message.Id, messageChannelKey, message.ChatSessionId)
                    ?? (messageChannelKey is not null
                        ? _commandResponseTracker?.TryConsume(message.Platform, message.Id, null, message.ChatSessionId)
                        : null))
                : null;

            var matchesCurrentStream = liveStream is not null
                && (liveStartUtc is null || timestamp.UtcDateTime >= liveStartUtc.Value);

            entries.Add(ChatMessageDataFactory.Create(
                message,
                platform,
                obsChat,
                timestamp.UtcDateTime,
                isFirstTime: false,
                isHistorical: true,
                commandResponse,
                matchesCurrentStream ? currentBroadcastId : null,
                matchesCurrentStream ? liveStream?.Id : null,
                matchesCurrentStream ? liveStream?.StartedAt : null,
                matchesCurrentStream ? liveStream?.Title : null));
        }

        var outcome = _historyManager.ImportHistory(@event.Scope, entries, @event.Moderations);
        var deletedIds = new HashSet<string>(outcome.RemovedMessageIds ?? [], StringComparer.Ordinal);
        if (@event.Moderations is not null)
        {
            foreach (var moderation in @event.Moderations)
            {
                if (moderation.Kind is ChatModerationActionKind.MessageDeleted or ChatModerationActionKind.MessageReplaced
                    && !string.IsNullOrWhiteSpace(moderation.TargetMessageId))
                {
                    deletedIds.Add(moderation.TargetMessageId);
                }
            }
        }

        if (deletedIds.Count > 0)
        {
            await _eventBus.PublishAsync(
                new ChatMessagesDeleted(outcome.Scope, [.. deletedIds]),
                cancellationToken).ConfigureAwait(false);
        }

        if (outcome.ModeratedAuthors is { Count: > 0 })
        {
            foreach (var (userId, cutoffUtc) in outcome.ModeratedAuthors)
            {
                await _eventBus.PublishAsync(
                    new ChatMessagesDeleted(outcome.Scope, [], userId, cutoffUtc == default ? null : cutoffUtc),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (outcome.AddedEntries.Count > 0)
        {
            await _eventBus.PublishAsync(
                new ChatHistoryLoaded(outcome.Scope, outcome.AddedEntries, outcome.MergedHistory),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
