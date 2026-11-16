using PoproshaykaBot.Core.Platforms;
using System.Text;

namespace PoproshaykaBot.Core.Chat.History;

public sealed class ChatHistoryBuffer
{
    public const int MaxDeletedMessageTombstones = 2048;
    public const int MaxAuthorModerationTombstones = 512;

    private sealed record Entry(ChatMessageData Message, long Sequence);
    private sealed record AuthorTombstone(DateTimeOffset CutoffUtc, DateTimeOffset RecordedAtUtc);

    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _deletedMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AuthorTombstone> _moderatedAuthors = new(StringComparer.Ordinal);

    private long _nextSequence;

    public ChatHistoryBuffer(ChatHistoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
        ChannelName = scope.ChannelName;
    }

    public ChatHistoryScope Scope { get; }

    public string ChannelName { get; private set; }

    public DateTimeOffset? ClearedAtUtc { get; private set; }

    public ulong ClearGeneration { get; private set; }

    public ulong MutationVersion { get; private set; }

    public int Count => _entries.Count;

    public bool HasAnyState => _entries.Count > 0 || ClearedAtUtc.HasValue || _deletedMessages.Count > 0 || _moderatedAuthors.Count > 0;

    public DateTimeOffset? OldestMessageTimestampUtc => _entries.Count > 0 ? ChatMessageDataFactory.ToUtcOffset(_entries[0].Message.Timestamp) : null;

    public DateTimeOffset? NewestMessageTimestampUtc => _entries.Count > 0 ? ChatMessageDataFactory.ToUtcOffset(_entries[^1].Message.Timestamp) : null;

    internal int EstimateOldestMessageBytes()
    {
        if (_entries.Count == 0)
        {
            return 0;
        }

        var message = _entries[0].Message;
        var textBytes = string.IsNullOrEmpty(message.Message) ? 0 : Encoding.UTF8.GetByteCount(message.Message);
        return textBytes + 256;
    }

    public void UpdateChannelName(string? channelName)
    {
        if (!string.IsNullOrWhiteSpace(channelName))
        {
            ChannelName = channelName.Trim();
        }
    }

    public bool TryAddLive(ChatMessageData message, DateTimeOffset minAllowedTimestampUtc, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Timestamp != default)
        {
            var timestampUtc = ChatMessageDataFactory.ToUtcOffset(message.Timestamp);
            if (timestampUtc < minAllowedTimestampUtc || IsBlockedByTombstone(message.MessageId, message.UserId, timestampUtc))
            {
                return false;
            }
        }
        else if (IsBlockedByTombstone(message.MessageId, message.UserId, DateTimeOffset.MinValue))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(message.MessageId) && !_messageIds.Add(message.MessageId))
        {
            return false;
        }

        var normalized = NormalizeEntry(message, isHistorical: false);
        InsertOrdered(normalized);
        TrimToMaxItems(maxItems);
        MutationVersion++;
        return true;
    }

    public IReadOnlyList<ChatMessageData> ImportHistory(
        IReadOnlyList<ChatMessageData> candidates,
        DateTimeOffset minAllowedTimestampUtc,
        DateTimeOffset? globalClearCutoffUtc,
        int maxItems,
        IReadOnlyList<ChatModerationAction>? batchModerations = null)
    {
        return ImportHistory(candidates, minAllowedTimestampUtc, globalClearCutoffUtc, maxItems, batchModerations, out _, out _);
    }

    public IReadOnlyList<ChatMessageData> ImportHistory(
        IReadOnlyList<ChatMessageData> candidates,
        DateTimeOffset minAllowedTimestampUtc,
        DateTimeOffset? globalClearCutoffUtc,
        int maxItems,
        IReadOnlyList<ChatModerationAction>? batchModerations,
        out IReadOnlyList<string> removedMessageIds,
        out IReadOnlyList<(string UserId, DateTimeOffset CutoffUtc)> moderatedAuthors)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var added = new List<ChatMessageData>();
        var allRemoved = new List<string>();
        var moderatedAuthorsList = new List<(string UserId, DateTimeOffset CutoffUtc)>();
        var mutated = false;

        if (batchModerations is not null)
        {
            foreach (var action in batchModerations)
            {
                if (action is null)
                {
                    continue;
                }

                if (ApplyModerationCore(action, action.CutoffUtc, out var stepRemoved))
                {
                    mutated = true;
                }

                if (stepRemoved.Count > 0)
                {
                    allRemoved.AddRange(stepRemoved);
                }

                if (action.Kind == ChatModerationActionKind.AuthorMessagesDeleted
                    && !string.IsNullOrWhiteSpace(action.TargetUserId))
                {
                    moderatedAuthorsList.Add((action.TargetUserId, action.CutoffUtc));
                }
            }
        }

        var effectiveClearCutoff = MaxNullable(ClearedAtUtc, globalClearCutoffUtc);

        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            var timestampUtc = candidate.Timestamp == default
                ? DateTimeOffset.MinValue
                : ChatMessageDataFactory.ToUtcOffset(candidate.Timestamp);

            if (candidate.Timestamp != default && timestampUtc < minAllowedTimestampUtc)
            {
                continue;
            }

            if (effectiveClearCutoff is { } clearCutoff && timestampUtc <= clearCutoff)
            {
                continue;
            }

            if (IsBlockedByTombstone(candidate.MessageId, candidate.UserId, timestampUtc))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.MessageId) && !_messageIds.Add(candidate.MessageId))
            {
                continue;
            }

            var entry = NormalizeEntry(candidate, isHistorical: true);
            InsertOrdered(entry);
            added.Add(entry);
            mutated = true;
        }

        if (TrimToMaxItems(maxItems))
        {
            mutated = true;
        }

        if (mutated)
        {
            MutationVersion++;
        }

        removedMessageIds = allRemoved;
        moderatedAuthors = moderatedAuthorsList;
        return added;
    }

    public IReadOnlyList<ChatMessageData> RestoreFromDocument(
        ChatHistoryCacheDocument document,
        DateTimeOffset minAllowedTimestampUtc,
        int maxItems,
        out bool prunedExpired,
        DateTimeOffset? globalClearCutoffUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        prunedExpired = false;
        UpdateChannelName(document.ChannelName);

        if (document.ClearedAtUtc is { } clearedAt)
        {
            if (clearedAt >= minAllowedTimestampUtc)
            {
                ClearedAtUtc = MaxNullable(ClearedAtUtc, clearedAt);
            }
            else
            {
                prunedExpired = true;
            }
        }

        if (document.DeletedMessages is not null)
        {
            foreach (var deleted in document.DeletedMessages)
            {
                if (deleted is null || string.IsNullOrWhiteSpace(deleted.MessageId))
                {
                    continue;
                }

                if (deleted.RecordedAtUtc < minAllowedTimestampUtc)
                {
                    prunedExpired = true;
                    continue;
                }

                RecordDeletedMessageTombstone(deleted.MessageId, deleted.RecordedAtUtc);
            }
        }

        if (document.ModeratedAuthors is not null)
        {
            foreach (var author in document.ModeratedAuthors)
            {
                if (author is null || string.IsNullOrWhiteSpace(author.UserId))
                {
                    continue;
                }

                if (author.RecordedAtUtc < minAllowedTimestampUtc)
                {
                    prunedExpired = true;
                    continue;
                }

                RecordAuthorTombstone(author.UserId, author.CutoffUtc, author.RecordedAtUtc);
            }
        }

        var messageList = document.Messages ?? [];
        var candidates = new List<ChatMessageData>(messageList.Count);

        foreach (var persisted in messageList)
        {
            if (persisted is null)
            {
                prunedExpired = true;
                continue;
            }

            var timestampUtc = persisted.Timestamp == default
                ? DateTimeOffset.MinValue
                : ChatMessageDataFactory.ToUtcOffset(persisted.Timestamp);

            if (persisted.Timestamp != default && timestampUtc < minAllowedTimestampUtc)
            {
                prunedExpired = true;
                continue;
            }

            candidates.Add(FromPersisted(persisted));
        }

        var restored = ImportHistory(candidates, minAllowedTimestampUtc, globalClearCutoffUtc, maxItems);
        if (restored.Count < candidates.Count)
        {
            prunedExpired = true;
        }

        return restored;
    }

    public bool ApplyModeration(ChatModerationAction action, DateTimeOffset nowUtc, out IReadOnlyList<string> removedMessageIds)
    {
        var changed = ApplyModerationCore(action, nowUtc, out removedMessageIds);
        if (changed)
        {
            MutationVersion++;
        }

        return changed;
    }

    public bool PruneExpired(DateTimeOffset minAllowedTimestampUtc)
    {
        var changed = false;

        if (ClearedAtUtc is { } clearedAt && clearedAt < minAllowedTimestampUtc)
        {
            ClearedAtUtc = null;
            changed = true;
        }

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Message.Timestamp != default && ChatMessageDataFactory.ToUtcOffset(entry.Message.Timestamp) < minAllowedTimestampUtc)
            {
                RemoveEntryAt(i);
                changed = true;
            }
        }

        foreach (var key in _deletedMessages.Where(pair => pair.Value < minAllowedTimestampUtc).Select(static pair => pair.Key).ToArray())
        {
            _deletedMessages.Remove(key);
            changed = true;
        }

        foreach (var key in _moderatedAuthors.Where(pair => pair.Value.RecordedAtUtc < minAllowedTimestampUtc).Select(static pair => pair.Key).ToArray())
        {
            _moderatedAuthors.Remove(key);
            changed = true;
        }

        if (changed)
        {
            MutationVersion++;
        }

        return changed;
    }

    public bool TrimToMaxItems(int maxItems)
    {
        var limit = Math.Max(1, maxItems);
        var removed = false;

        while (_entries.Count > limit)
        {
            RemoveEntryAt(0);
            removed = true;
        }

        if (removed)
        {
            MutationVersion++;
        }

        return removed;
    }

    public int EvictOldestMessages(int maxCount)
    {
        if (_entries.Count == 0 || maxCount <= 0)
        {
            return 0;
        }

        var evictCount = Math.Min(maxCount, _entries.Count);
        for (var i = 0; i < evictCount; i++)
        {
            if (!string.IsNullOrWhiteSpace(_entries[i].Message.MessageId))
            {
                _messageIds.Remove(_entries[i].Message.MessageId);
            }
        }

        _entries.RemoveRange(0, evictCount);
        MutationVersion++;
        return evictCount;
    }

    public bool Clear(DateTimeOffset nowUtc, out string? channelName)
    {
        var hadMessages = _entries.Count > 0;
        channelName = hadMessages ? (string.IsNullOrEmpty(ChannelName) ? Scope.ChannelName : ChannelName) : null;

        _entries.Clear();
        _messageIds.Clear();
        ClearedAtUtc = MaxNullable(ClearedAtUtc, nowUtc);
        ClearGeneration++;
        MutationVersion++;

        return hadMessages;
    }

    public IReadOnlyList<ChatMessageData> GetSnapshot()
    {
        var result = new ChatMessageData[_entries.Count];
        for (var i = 0; i < _entries.Count; i++)
        {
            result[i] = _entries[i].Message;
        }

        return result;
    }

    public ChatHistoryCacheDocument ToDocument()
    {
        var document = new ChatHistoryCacheDocument
        {
            SchemaVersion = ChatHistoryCacheDocument.CurrentSchemaVersion,
            Platform = Scope.Platform,
            ChannelId = Scope.ChannelId,
            SessionScopeId = Scope.SessionScopeId,
            ChannelName = ChannelName,
            ClearedAtUtc = ClearedAtUtc,
        };

        foreach (var (messageId, recordedAtUtc) in _deletedMessages)
        {
            document.DeletedMessages.Add(new()
            {
                MessageId = messageId,
                RecordedAtUtc = recordedAtUtc,
            });
        }

        foreach (var (userId, tombstone) in _moderatedAuthors)
        {
            document.ModeratedAuthors.Add(new()
            {
                UserId = userId,
                CutoffUtc = tombstone.CutoffUtc,
                RecordedAtUtc = tombstone.RecordedAtUtc,
            });
        }

        foreach (var entry in _entries)
        {
            document.Messages.Add(ToPersisted(entry.Message));
        }

        return document;
    }

    private bool ApplyModerationCore(ChatModerationAction action, DateTimeOffset nowUtc, out IReadOnlyList<string> removedMessageIds)
    {
        var removed = new List<string>();
        var changed = false;

        switch (action.Kind)
        {
            case ChatModerationActionKind.MessageDeleted or ChatModerationActionKind.MessageReplaced:
                if (!string.IsNullOrWhiteSpace(action.TargetMessageId))
                {
                    var isSameIdReplacement = action.Kind == ChatModerationActionKind.MessageReplaced
                        && string.Equals(action.TargetMessageId, action.ReplacementMessageId, StringComparison.Ordinal);

                    if (!isSameIdReplacement && RecordDeletedMessageTombstone(action.TargetMessageId, nowUtc))
                    {
                        changed = true;
                    }

                    for (var i = _entries.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(_entries[i].Message.MessageId, action.TargetMessageId, StringComparison.Ordinal))
                        {
                            if (!string.IsNullOrWhiteSpace(_entries[i].Message.MessageId))
                            {
                                removed.Add(_entries[i].Message.MessageId);
                            }

                            RemoveEntryAt(i);
                            changed = true;
                        }
                    }
                }

                break;

            case ChatModerationActionKind.AuthorMessagesDeleted:
                if (!string.IsNullOrWhiteSpace(action.TargetUserId))
                {
                    var cutoffUtc = action.CutoffUtc == default ? nowUtc : action.CutoffUtc;
                    if (RecordAuthorTombstone(action.TargetUserId, cutoffUtc, nowUtc))
                    {
                        changed = true;
                    }

                    for (var i = _entries.Count - 1; i >= 0; i--)
                    {
                        var message = _entries[i].Message;
                        if (string.Equals(message.UserId, action.TargetUserId, StringComparison.Ordinal)
                            && (message.Timestamp == default || ChatMessageDataFactory.ToUtcOffset(message.Timestamp) <= cutoffUtc))
                        {
                            if (!string.IsNullOrWhiteSpace(message.MessageId))
                            {
                                removed.Add(message.MessageId);
                            }

                            RemoveEntryAt(i);
                            changed = true;
                        }
                    }
                }

                break;
        }

        removedMessageIds = removed;
        return changed;
    }

    private bool RecordDeletedMessageTombstone(string messageId, DateTimeOffset recordedAtUtc)
    {
        if (_deletedMessages.TryGetValue(messageId, out var existing) && existing >= recordedAtUtc)
        {
            return false;
        }

        _deletedMessages[messageId] = recordedAtUtc;

        while (_deletedMessages.Count > MaxDeletedMessageTombstones)
        {
            var oldestKey = _deletedMessages.MinBy(static pair => pair.Value).Key;
            _deletedMessages.Remove(oldestKey);
        }

        return true;
    }

    private bool RecordAuthorTombstone(string userId, DateTimeOffset cutoffUtc, DateTimeOffset recordedAtUtc)
    {
        if (_moderatedAuthors.TryGetValue(userId, out var existing)
            && existing.CutoffUtc >= cutoffUtc
            && existing.RecordedAtUtc >= recordedAtUtc)
        {
            return false;
        }

        var mergedCutoff = existing is null ? cutoffUtc : Max(existing.CutoffUtc, cutoffUtc);
        var mergedRecorded = existing is null ? recordedAtUtc : Max(existing.RecordedAtUtc, recordedAtUtc);
        _moderatedAuthors[userId] = new(mergedCutoff, mergedRecorded);

        while (_moderatedAuthors.Count > MaxAuthorModerationTombstones)
        {
            var oldestKey = _moderatedAuthors.MinBy(static pair => pair.Value.RecordedAtUtc).Key;
            _moderatedAuthors.Remove(oldestKey);
        }

        return true;
    }

    private bool IsBlockedByTombstone(string messageId, string userId, DateTimeOffset timestampUtc)
    {
        if (!string.IsNullOrWhiteSpace(messageId) && _deletedMessages.ContainsKey(messageId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(userId)
            && _moderatedAuthors.TryGetValue(userId, out var authorTombstone)
            && timestampUtc <= authorTombstone.CutoffUtc)
        {
            return true;
        }

        return false;
    }

    private void InsertOrdered(ChatMessageData message)
    {
        var sequence = _nextSequence++;
        var newEntry = new Entry(message, sequence);
        var insertIndex = _entries.Count;
        var messageTicks = ChatMessageDataFactory.ToUtcTicks(message.Timestamp);

        while (insertIndex > 0 && ChatMessageDataFactory.ToUtcTicks(_entries[insertIndex - 1].Message.Timestamp) > messageTicks)
        {
            insertIndex--;
        }

        _entries.Insert(insertIndex, newEntry);
    }

    private void RemoveEntryAt(int index)
    {
        var removed = _entries[index].Message;
        _entries.RemoveAt(index);

        if (!string.IsNullOrWhiteSpace(removed.MessageId))
        {
            _messageIds.Remove(removed.MessageId);
        }
    }

    private ChatMessageData NormalizeEntry(ChatMessageData message, bool isHistorical)
    {
        if (message.IsHistorical == isHistorical
            && !string.IsNullOrEmpty(message.Channel)
            && !string.IsNullOrEmpty(message.ChannelId))
        {
            return message;
        }

        return new()
        {
            Platform = message.Platform,
            Channel = string.IsNullOrEmpty(message.Channel) ? ChannelName : message.Channel,
            ChannelId = string.IsNullOrEmpty(message.ChannelId) ? Scope.ChannelId : message.ChannelId,
            ChatSessionId = message.ChatSessionId ?? Scope.SessionScopeId,
            MessageId = message.MessageId,
            Timestamp = message.Timestamp,
            UserId = message.UserId,
            DisplayName = message.DisplayName,
            Message = message.Message,
            MessageType = message.MessageType,
            Status = message.Status,
            IsFirstTime = message.IsFirstTime,
            IsHistorical = isHistorical,
            IsBot = message.IsBot,
            CommandResponse = message.CommandResponse,
            Color = message.Color,
            Emotes = message.Emotes,
            Badges = message.Badges,
            BadgeUrls = message.BadgeUrls,
        };
    }

    private static PersistedChatMessage ToPersisted(ChatMessageData message)
    {
        return new()
        {
            Platform = message.Platform,
            Channel = message.Channel,
            ChannelId = message.ChannelId,
            ChatSessionId = message.ChatSessionId,
            MessageId = message.MessageId,
            Timestamp = ChatMessageDataFactory.EnsureUtc(message.Timestamp),
            UserId = message.UserId,
            DisplayName = message.DisplayName,
            Message = message.Message,
            MessageType = message.MessageType,
            Status = message.Status,
            IsFirstTime = message.IsFirstTime,
            IsBot = message.IsBot,
            CommandResponse = message.CommandResponse is { } mark
                ? new()
                {
                    Canonical = mark.Canonical,
                    Target = mark.Target,
                }
                : null,
            Color = message.Color,
            Emotes = [.. message.Emotes],
            Badges = [.. message.Badges.Select(static b => new PersistedBadgeEntry { Key = b.Key, Value = b.Value })],
            BadgeUrls = new(message.BadgeUrls, StringComparer.Ordinal),
        };
    }

    private ChatMessageData FromPersisted(PersistedChatMessage persisted)
    {
        return new()
        {
            Platform = persisted.Platform,
            Channel = string.IsNullOrEmpty(persisted.Channel) ? ChannelName : persisted.Channel,
            ChannelId = string.IsNullOrEmpty(persisted.ChannelId) ? Scope.ChannelId : persisted.ChannelId,
            ChatSessionId = persisted.ChatSessionId ?? Scope.SessionScopeId,
            MessageId = persisted.MessageId ?? string.Empty,
            Timestamp = ChatMessageDataFactory.EnsureUtc(persisted.Timestamp),
            UserId = persisted.UserId ?? string.Empty,
            DisplayName = persisted.DisplayName ?? string.Empty,
            Message = persisted.Message ?? string.Empty,
            MessageType = persisted.MessageType,
            Status = persisted.Status,
            IsFirstTime = persisted.IsFirstTime,
            IsHistorical = true,
            IsBot = persisted.IsBot,
            CommandResponse = persisted.CommandResponse is { } mark
                ? new(mark.Canonical ?? string.Empty, mark.Target)
                : null,
            Color = persisted.Color,
            Emotes = persisted.Emotes is null ? [] : [.. persisted.Emotes.Where(static e => e is not null)],
            Badges = persisted.Badges is null
                ? []
                : [.. persisted.Badges.Where(static b => b is not null).Select(static b => new KeyValuePair<string, string>(b.Key ?? string.Empty, b.Value ?? string.Empty))],
            BadgeUrls = persisted.BadgeUrls is null
                ? new(StringComparer.Ordinal)
                : new(persisted.BadgeUrls, StringComparer.Ordinal),
        };
    }

    private static DateTimeOffset? MaxNullable(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left >= right ? left : right;
    }
}
