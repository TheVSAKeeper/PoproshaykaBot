using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class ChatModerationHandler : IEventHandler<RawChatModerationReceived>, IEventSubscriber, IDisposable
{
    private readonly ChatHistoryManager _historyManager;
    private readonly IEventBus _eventBus;
    private readonly IDisposable _subscription;

    public ChatModerationHandler(ChatHistoryManager historyManager, IEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(historyManager);
        ArgumentNullException.ThrowIfNull(eventBus);

        _historyManager = historyManager;
        _eventBus = eventBus;
        _subscription = eventBus.Subscribe(this);
    }

    public async Task HandleAsync(RawChatModerationReceived @event, CancellationToken cancellationToken)
    {
        var action = @event.Action;
        var removedIds = _historyManager.ApplyModeration(@event.Scope, action);

        var idsToNotify = removedIds.Count > 0
            ? removedIds
            : action.Kind is ChatModerationActionKind.MessageDeleted or ChatModerationActionKind.MessageReplaced
              && !string.IsNullOrWhiteSpace(action.TargetMessageId)
                ? [action.TargetMessageId]
                : [];

        if (idsToNotify.Count > 0 || action.Kind == ChatModerationActionKind.AuthorMessagesDeleted)
        {
            var isAuthorModeration = action.Kind == ChatModerationActionKind.AuthorMessagesDeleted;
            var allowReplacement = action.Kind == ChatModerationActionKind.MessageReplaced
                && string.Equals(action.TargetMessageId, action.ReplacementMessageId, StringComparison.Ordinal);

            await _eventBus.PublishAsync(
                new ChatMessagesDeleted(
                    @event.Scope,
                    idsToNotify,
                    isAuthorModeration ? action.TargetUserId : null,
                    isAuthorModeration && action.CutoffUtc != default ? action.CutoffUtc : null,
                    allowReplacement),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
