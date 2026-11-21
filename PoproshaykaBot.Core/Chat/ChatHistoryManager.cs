using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatHistoryManager : IDisposable
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly IChatHistorySettingsProvider? _historySettingsProvider;
    private readonly IChatHistoryPersistenceSink? _persistenceQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly PlatformChannelRegistry? _channelRegistry;
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly IDisposable _activeChannelSubscription;
    private readonly Lock _sync = new();
    private readonly Dictionary<ChatHistoryScope, ChatHistoryBuffer> _buffers = [];
    private readonly Dictionary<StreamingPlatform, ChatHistoryScope> _activeScopesByPlatform = [];
    private readonly Dictionary<StreamingPlatform, string> _activeChannelIdsByPlatform = [];

    private DateTimeOffset? _globalClearedAtUtc;
    private bool _disposed;

    public ChatHistoryManager(
        SettingsManager settingsManager,
        IEventBus eventBus,
        IChatHistorySettingsProvider? historySettingsProvider = null,
        IChatHistoryPersistenceSink? persistenceQueue = null,
        TimeProvider? timeProvider = null,
        ChannelCatalogStore? channelCatalogStore = null,
        PlatformChannelRegistry? channelRegistry = null,
        ITargetChannelProvider? targetChannelProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(eventBus);

        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _historySettingsProvider = historySettingsProvider;
        _persistenceQueue = persistenceQueue;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _channelCatalogStore = channelCatalogStore;
        _channelRegistry = channelRegistry;
        _targetChannelProvider = targetChannelProvider;

        _activeChannelSubscription = _eventBus.Subscribe<ActiveChannelChanged>(OnActiveChannelChanged);

        if (_historySettingsProvider is not null)
        {
            _historySettingsProvider.Changed += OnHistorySettingsChanged;
        }
    }

    public void AddMessage(ChatMessageData chatMessage, string channel = "")
    {
        ArgumentNullException.ThrowIfNull(chatMessage);

        var scope = ChatHistoryScope.FromMessage(chatMessage, channel);
        var nowUtc = _timeProvider.GetUtcNow();
        var minAllowed = nowUtc - RetentionPeriod;
        var maxItems = GetMaxItems();
        bool added;

        lock (_sync)
        {
            var buffer = GetOrCreateBuffer(scope);
            buffer.UpdateChannelName(scope.ChannelName);
            MarkActiveScopeUnderLock(scope);
            added = buffer.TryAddLive(chatMessage, minAllowed, maxItems);
        }

        if (added && !string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            _persistenceQueue?.MarkDirty(scope);
        }
    }

    public ChatHistoryImportOutcome ImportHistory(
        ChatHistoryScope scope,
        IReadOnlyList<ChatMessageData> candidates,
        IReadOnlyList<ChatModerationAction>? batchModerations = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(candidates);

        var nowUtc = _timeProvider.GetUtcNow();
        var minAllowed = nowUtc - RetentionPeriod;
        var maxItems = GetMaxItems();

        IReadOnlyList<ChatMessageData> addedEntries;
        IReadOnlyList<ChatMessageData> mergedHistory;
        IReadOnlyList<string> removedMessageIds;
        IReadOnlyList<(string UserId, DateTimeOffset CutoffUtc)> moderatedAuthors;
        bool shouldMarkDirty;

        lock (_sync)
        {
            var buffer = GetOrCreateBuffer(scope);
            buffer.UpdateChannelName(scope.ChannelName);
            MarkActiveScopeUnderLock(scope);
            var versionBefore = buffer.MutationVersion;

            addedEntries = buffer.ImportHistory(
                candidates,
                minAllowed,
                _globalClearedAtUtc,
                maxItems,
                batchModerations,
                out removedMessageIds,
                out moderatedAuthors);
            mergedHistory = buffer.GetSnapshot();
            shouldMarkDirty = buffer.MutationVersion != versionBefore;
        }

        if (shouldMarkDirty && !string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            _persistenceQueue?.MarkDirty(scope);
        }

        return new(scope, addedEntries, mergedHistory, removedMessageIds, moderatedAuthors);
    }

    public IReadOnlyList<string> ApplyModeration(
        ChatHistoryScope scope,
        ChatModerationAction action)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(action);

        var nowUtc = action.CutoffUtc == default ? _timeProvider.GetUtcNow() : action.CutoffUtc;
        IReadOnlyList<string> removedMessageIds;
        bool changed;

        lock (_sync)
        {
            var buffer = GetOrCreateBuffer(scope);
            buffer.UpdateChannelName(scope.ChannelName);
            MarkActiveScopeUnderLock(scope);
            changed = buffer.ApplyModeration(action, nowUtc, out removedMessageIds);
        }

        if (changed && !string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            _persistenceQueue?.MarkDirty(scope);
        }

        return removedMessageIds;
    }

    public IReadOnlyList<ChatMessageData> GetHistory()
    {
        List<ChatMessageData> all = [];

        lock (_sync)
        {
            foreach (var buffer in _buffers.Values)
            {
                all.AddRange(buffer.GetSnapshot());
            }
        }

        all.Sort(static (left, right) => ChatMessageDataFactory.ToUtcTicks(left.Timestamp).CompareTo(ChatMessageDataFactory.ToUtcTicks(right.Timestamp)));
        return all;
    }

    public IReadOnlyList<ChatMessageData> GetActiveHistory()
    {
        List<ChatMessageData> all = [];

        lock (_sync)
        {
            foreach (var buffer in _buffers.Values)
            {
                if (IsActiveScopeUnderLock(buffer.Scope))
                {
                    all.AddRange(buffer.GetSnapshot());
                }
            }
        }

        all.Sort(static (left, right) => ChatMessageDataFactory.ToUtcTicks(left.Timestamp).CompareTo(ChatMessageDataFactory.ToUtcTicks(right.Timestamp)));
        return all;
    }

    public bool IsActiveScope(ChatHistoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        lock (_sync)
        {
            return IsActiveScopeUnderLock(scope);
        }
    }

    public bool IsActiveChannel(StreamingPlatform platform, string? channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return true;
        }

        var normalizedId = NormalizeChannelId(platform, channelId);

        lock (_sync)
        {
            var activeChannelId = ResolveAuthoritativeChannelIdUnderLock(platform);
            if (activeChannelId is not null)
            {
                return string.Equals(normalizedId, activeChannelId, StringComparison.Ordinal);
            }

            return !_activeScopesByPlatform.TryGetValue(platform, out var active)
                || string.Equals(normalizedId, active.ChannelId, StringComparison.Ordinal);
        }
    }

    public IReadOnlyList<ChatMessageData> GetHistory(ChatHistoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        lock (_sync)
        {
            return _buffers.TryGetValue(scope, out var buffer)
                ? buffer.GetSnapshot()
                : [];
        }
    }

    public ChatHistoryClearResult ClearHistory()
    {
        return ClearHistoryCore(static _ => true, clearUnloadedScopes: true, setGlobalCleared: true);
    }

    public ChatHistoryClearResult ClearHistory(StreamingPlatform platform, string? channel = null)
    {
        var trimmedChannel = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        return ClearHistoryCore(
            buffer => buffer.Scope.Platform == platform
                && (trimmedChannel is null
                    || string.Equals(buffer.ChannelName, trimmedChannel, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(buffer.Scope.ChannelId, trimmedChannel, StringComparison.OrdinalIgnoreCase)),
            clearUnloadedScopes: false,
            setGlobalCleared: false);
    }

    private ChatHistoryClearResult ClearHistoryCore(
        Func<ChatHistoryBuffer, bool> predicate,
        bool clearUnloadedScopes,
        bool setGlobalCleared)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        List<(StreamingPlatform Platform, string ChannelName)> clearedChats = [];
        List<ChatHistoryClearTarget> diskTargets = [];

        lock (_sync)
        {
            if (setGlobalCleared)
            {
                _globalClearedAtUtc = _globalClearedAtUtc is { } existing && existing > nowUtc
                    ? existing
                    : nowUtc;
            }

            foreach (var buffer in _buffers.Values)
            {
                if (!predicate(buffer))
                {
                    continue;
                }

                var hadState = buffer.HasAnyState;
                if (buffer.Clear(nowUtc, out var channelName) && channelName is not null)
                {
                    clearedChats.Add((buffer.Scope.Platform, channelName));
                }

                if (!string.IsNullOrWhiteSpace(buffer.Scope.ChannelId) && (hadState || !clearUnloadedScopes))
                {
                    var targetScope = buffer.Scope;
                    diskTargets.Add(new(
                        targetScope,
                        buffer.ClearGeneration,
                        buffer.MutationVersion,
                        buffer.ToDocument(),
                        hadState,
                        () => TryCapturePersistenceSnapshot(targetScope, out var current) ? current : null));
                }
            }
        }

        var result = _persistenceQueue is not null
            ? _persistenceQueue.ClearScopes(diskTargets, clearUnloadedScopes)
            : new ChatHistoryClearResult(true);

        foreach (var (platform, channelName) in clearedChats.Distinct())
        {
            _ = _eventBus.PublishAsync(new ChatHistoryCleared(platform, channelName, nowUtc));
        }

        return result;
    }

    internal ChatHistoryImportOutcome RestoreFromDocument(
        ChatHistoryScope scope,
        ChatHistoryCacheDocument document,
        out bool prunedExpired)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var minAllowed = nowUtc - RetentionPeriod;
        var maxItems = GetMaxItems();

        lock (_sync)
        {
            var buffer = GetOrCreateBuffer(scope);
            var added = buffer.RestoreFromDocument(document, minAllowed, maxItems, out prunedExpired, _globalClearedAtUtc);
            UpdateRestoredActiveScopeUnderLock(buffer);
            var merged = buffer.GetSnapshot();
            return new(scope, added, merged);
        }
    }

    internal bool TryCapturePersistenceSnapshot(
        ChatHistoryScope scope,
        out ChatHistoryPersistenceSnapshot snapshot)
    {
        lock (_sync)
        {
            if (!_buffers.TryGetValue(scope, out var buffer))
            {
                snapshot = default!;
                return false;
            }

            snapshot = new(
                scope,
                buffer.ClearGeneration,
                buffer.MutationVersion,
                buffer.HasAnyState,
                buffer.ToDocument());
            return true;
        }
    }

    internal bool IsClearGenerationCurrent(ChatHistoryScope scope, ulong expectedClearGeneration)
    {
        lock (_sync)
        {
            return _buffers.TryGetValue(scope, out var buffer)
                && buffer.ClearGeneration == expectedClearGeneration;
        }
    }

    internal IReadOnlyList<ChatHistoryScope> PruneExpired()
    {
        var minAllowed = _timeProvider.GetUtcNow() - RetentionPeriod;
        List<ChatHistoryScope> modified = [];

        lock (_sync)
        {
            foreach (var buffer in _buffers.Values)
            {
                if (buffer.PruneExpired(minAllowed))
                {
                    modified.Add(buffer.Scope);
                }
            }
        }

        return modified;
    }

    internal bool TryEvictOldestAcrossWritableScopes(
        IReadOnlySet<string> excludedFileNames,
        out ChatHistoryScope? evictedScope,
        out int estimatedMessageBytes)
    {
        lock (_sync)
        {
            ChatHistoryBuffer? oldestBuffer = null;
            var oldestTimestamp = DateTimeOffset.MaxValue;

            foreach (var buffer in _buffers.Values)
            {
                if (buffer.Count == 0
                    || string.IsNullOrWhiteSpace(buffer.Scope.ChannelId)
                    || excludedFileNames.Contains(buffer.Scope.ComputeCacheFileName()))
                {
                    continue;
                }

                var candidateTimestamp = buffer.OldestMessageTimestampUtc ?? DateTimeOffset.MaxValue;
                if (oldestBuffer is null || candidateTimestamp < oldestTimestamp)
                {
                    oldestBuffer = buffer;
                    oldestTimestamp = candidateTimestamp;
                }
            }

            if (oldestBuffer is null)
            {
                evictedScope = null;
                estimatedMessageBytes = 0;
                return false;
            }

            estimatedMessageBytes = oldestBuffer.EstimateOldestMessageBytes();
            if (oldestBuffer.EvictOldestMessages(1) == 0)
            {
                evictedScope = null;
                estimatedMessageBytes = 0;
                return false;
            }

            evictedScope = oldestBuffer.Scope;
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeChannelSubscription.Dispose();

        if (_historySettingsProvider is not null)
        {
            _historySettingsProvider.Changed -= OnHistorySettingsChanged;
        }
    }

    private void OnActiveChannelChanged(ActiveChannelChanged @event)
    {
        if (string.IsNullOrWhiteSpace(@event.NewChannel.ChannelId))
        {
            return;
        }

        var normalizedId = NormalizeChannelId(@event.Platform, @event.NewChannel.ChannelId);

        lock (_sync)
        {
            _activeChannelIdsByPlatform[@event.Platform] = normalizedId;
            SelectNewestBufferForChannelUnderLock(@event.Platform, normalizedId);
        }
    }

    private void OnHistorySettingsChanged(Settings.Chat.ChatSettings updated)
    {
        var maxItems = Math.Max(1, updated.HistoryMaxItems);
        List<ChatHistoryScope> trimmed = [];

        lock (_sync)
        {
            foreach (var buffer in _buffers.Values)
            {
                if (buffer.TrimToMaxItems(maxItems))
                {
                    trimmed.Add(buffer.Scope);
                }
            }
        }

        foreach (var scope in trimmed)
        {
            _persistenceQueue?.MarkDirty(scope);
        }
    }

    private ChatHistoryBuffer GetOrCreateBuffer(ChatHistoryScope scope)
    {
        if (!_buffers.TryGetValue(scope, out var buffer))
        {
            buffer = new(scope);
            _buffers[scope] = buffer;
        }

        return buffer;
    }

    private void MarkActiveScopeUnderLock(ChatHistoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            return;
        }

        var activeChannelId = ResolveAuthoritativeChannelIdUnderLock(scope.Platform);
        if (activeChannelId is not null
            && !string.Equals(scope.ChannelId, activeChannelId, StringComparison.Ordinal))
        {
            return;
        }

        _activeScopesByPlatform[scope.Platform] = scope;
    }

    private void UpdateRestoredActiveScopeUnderLock(ChatHistoryBuffer restoredBuffer)
    {
        var scope = restoredBuffer.Scope;
        if (string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            return;
        }

        var activeChannelId = ResolveAuthoritativeChannelIdUnderLock(scope.Platform);
        if (activeChannelId is not null
            && !string.Equals(scope.ChannelId, activeChannelId, StringComparison.Ordinal))
        {
            return;
        }

        if (!_activeScopesByPlatform.TryGetValue(scope.Platform, out var currentActive))
        {
            _activeScopesByPlatform[scope.Platform] = scope;
            return;
        }

        if (activeChannelId is not null
            && !string.Equals(currentActive.ChannelId, activeChannelId, StringComparison.Ordinal))
        {
            _activeScopesByPlatform[scope.Platform] = scope;
            return;
        }

        var restoredNewest = restoredBuffer.NewestMessageTimestampUtc;
        var currentNewest = _buffers.TryGetValue(currentActive, out var currentBuffer)
            ? currentBuffer.NewestMessageTimestampUtc
            : null;

        if (restoredNewest is { } r && (currentNewest is null || r > currentNewest))
        {
            _activeScopesByPlatform[scope.Platform] = scope;
        }
    }

    private bool IsActiveScopeUnderLock(ChatHistoryScope scope)
    {
        var activeChannelId = ResolveAuthoritativeChannelIdUnderLock(scope.Platform);
        if (activeChannelId is not null)
        {
            if (!string.Equals(scope.ChannelId, activeChannelId, StringComparison.Ordinal))
            {
                return false;
            }

            if (_activeScopesByPlatform.TryGetValue(scope.Platform, out var activeForChannel)
                && string.Equals(activeForChannel.ChannelId, activeChannelId, StringComparison.Ordinal))
            {
                return activeForChannel.Equals(scope);
            }

            return true;
        }

        return !_activeScopesByPlatform.TryGetValue(scope.Platform, out var active) || active.Equals(scope);
    }

    private string? ResolveAuthoritativeChannelIdUnderLock(StreamingPlatform platform)
    {
        if (_activeChannelIdsByPlatform.TryGetValue(platform, out var explicitId))
        {
            return explicitId;
        }

        var boundId = _channelRegistry?.GetBinding(platform)?.Key.ChannelId;
        if (!string.IsNullOrWhiteSpace(boundId))
        {
            return NormalizeChannelId(platform, boundId);
        }

        if (platform == StreamingPlatform.Twitch && _targetChannelProvider?.Current.IsDebugSession == true)
        {
            return null;
        }

        var catalogId = _channelCatalogStore?.GetActiveChannel(platform)?.ChannelId;
        if (!string.IsNullOrWhiteSpace(catalogId))
        {
            return NormalizeChannelId(platform, catalogId);
        }

        return null;
    }

    private void SelectNewestBufferForChannelUnderLock(StreamingPlatform platform, string normalizedChannelId)
    {
        ChatHistoryBuffer? best = null;

        foreach (var buffer in _buffers.Values)
        {
            if (buffer.Scope.Platform != platform
                || !string.Equals(buffer.Scope.ChannelId, normalizedChannelId, StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null)
            {
                best = buffer;
                continue;
            }

            var candidateNewest = buffer.NewestMessageTimestampUtc;
            var bestNewest = best.NewestMessageTimestampUtc;
            if (candidateNewest is { } c && (bestNewest is null || c > bestNewest))
            {
                best = buffer;
            }
        }

        if (best is not null)
        {
            _activeScopesByPlatform[platform] = best.Scope;
        }
        else
        {
            _activeScopesByPlatform.Remove(platform);
        }
    }

    private static string NormalizeChannelId(StreamingPlatform platform, string channelId)
    {
        var trimmed = channelId.Trim();
        return platform == StreamingPlatform.Twitch ? trimmed.ToLowerInvariant() : trimmed;
    }

    private int GetMaxItems()
    {
        if (_historySettingsProvider is not null)
        {
            return Math.Max(1, _historySettingsProvider.GetCurrent().HistoryMaxItems);
        }

        return Math.Max(1, _settingsManager.Current.ChatHistoryMaxItems);
    }
}
