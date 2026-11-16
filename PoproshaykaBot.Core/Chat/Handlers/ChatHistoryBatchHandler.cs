using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ChatHistoryBatchHandler : IEventHandler<RawChatHistoryReceived>, IEventSubscriber, IDisposable
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly ObsChatStore _obsChatStore;
    private readonly ChatHistoryManager _historyManager;
    private readonly IEventBus _eventBus;
    private readonly CommandResponseTracker? _commandResponseTracker;
    private readonly IDisposable _subscription;

    public ChatHistoryBatchHandler(
        IChatPlatformRegistry platformRegistry,
        ObsChatStore obsChatStore,
        ChatHistoryManager historyManager,
        IEventBus eventBus,
        CommandResponseTracker? commandResponseTracker = null)
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
        _subscription = eventBus.Subscribe(this);
    }

    public async Task HandleAsync(RawChatHistoryReceived @event, CancellationToken cancellationToken)
    {
        if (!_platformRegistry.TryGet(@event.Platform, out var platform)
            || (platform.Capabilities & PlatformCapabilities.ServerChatHistory) == PlatformCapabilities.None)
        {
            return;
        }

        var obsChat = _obsChatStore.Load();
        var entries = new List<ChatMessageData>(@event.Messages.Count);

        foreach (var (message, timestamp) in @event.Messages)
        {
            var commandResponse = message.IsBot
                ? _commandResponseTracker?.TryConsume(message.Platform, message.Id)
                : null;

            entries.Add(ChatMessageDataFactory.Create(
                message,
                platform,
                obsChat,
                timestamp.UtcDateTime,
                isFirstTime: false,
                isHistorical: true,
                commandResponse));
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
