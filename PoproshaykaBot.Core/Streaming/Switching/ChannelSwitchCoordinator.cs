using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Streaming.Switching;

public enum ChannelSwitchOutcome
{
    Switched = 0,
    ChannelNotFound = 1,
    TargetNoLongerLive = 2,
    TargetChatUnreachable = 3,
    FailedKeptPrevious = 4,
    FailedNoActiveChat = 5,
    BlockedByDebugSession = 6,
}

public sealed record ChannelSwitchResult(
    ChannelSwitchOutcome Outcome,
    ChannelKey? ActiveChannel,
    string? ErrorMessage = null)
{
    public bool Succeeded => Outcome == ChannelSwitchOutcome.Switched;
}

public interface IChannelSwitchCoordinator
{
    AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform);

    IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts();

    Task<ChannelSwitchResult> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        CancellationToken cancellationToken = default);

    Task<ChannelSwitchResult> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        bool preservePreselectedNext,
        CancellationToken cancellationToken = default);

    Task<ChannelSwitchResult> SwitchCatalogChannelAsync(
        ChannelCatalogEntry targetChannel,
        CancellationToken cancellationToken = default);

    Task<ChannelSwitchResult?> SetPreselectedNextAsync(
        StreamingPlatform platform,
        ChannelKey? targetChannel,
        CancellationToken cancellationToken = default);

    Task SetSwitchModeAsync(
        StreamingPlatform platform,
        ChannelSwitchMode mode,
        CancellationToken cancellationToken = default);

    Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(
        StreamingPlatform platform,
        ChannelKey candidateChannel,
        CancellationToken cancellationToken = default);

    Task DismissAutoDiscoverPromptAsync(
        StreamingPlatform platform,
        CancellationToken cancellationToken = default);

    Task EvaluateStartupPendingAsync(
        StreamingPlatform platform,
        CancellationToken cancellationToken = default);
}

public sealed class ChannelSwitchCoordinator :
    IChannelSwitchCoordinator,
    IEventHandler<ChannelStreamObserved>,
    IEventSubscriber,
    IDisposable
{
    private sealed record ServicedStreamKey(ChannelKey Channel, string StreamKey);

    private sealed class PlatformSwitchSlot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Lock Sync { get; } = new();
        public long Generation { get; set; }
        public HashSet<ChannelKey> BaselineCaptured { get; } = [];
        public HashSet<ServicedStreamKey> BaselineStreams { get; } = [];
        public HashSet<ServicedStreamKey> ServicedStreams { get; } = [];
        public HashSet<ServicedStreamKey> SeenCandidates { get; } = [];
        public HashSet<ChannelKey> RequireOfflineBeforeRediscover { get; } = [];
        public HashSet<ServicedStreamKey> FailedPreselectedNotified { get; } = [];
        public List<AutoDiscoverCandidate> PendingCandidates { get; } = [];
        public ChannelKey? LastPromptActiveChannel { get; set; }
    }

    private readonly ChannelCatalogStore _catalogStore;
    private readonly PlatformChannelRegistry _channelRegistry;
    private readonly ChannelCatalogReconciler _reconciler;
    private readonly IPlatformAuthRegistry _authRegistry;
    private readonly IBroadcastChannelLifecycle _broadcastLifecycle;
    private readonly BotConnectionManager? _connectionManager;
    private readonly ChatPipelineHandler? _chatPipelineHandler;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelSwitchCoordinator> _logger;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelMonitor> _monitors;
    private readonly ConcurrentDictionary<StreamingPlatform, PlatformSwitchSlot> _slots = new();
    private readonly IDisposable _observedSubscription;

    public ChannelSwitchCoordinator(
        ChannelCatalogStore catalogStore,
        PlatformChannelRegistry channelRegistry,
        ChannelCatalogReconciler reconciler,
        IPlatformAuthRegistry authRegistry,
        IBroadcastChannelLifecycle broadcastLifecycle,
        IEnumerable<IPlatformChannelAdapter> adapters,
        IEnumerable<IPlatformChannelMonitor> monitors,
        ITargetChannelProvider targetChannelProvider,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ChannelSwitchCoordinator> logger,
        BotConnectionManager? connectionManager = null,
        ChatPipelineHandler? chatPipelineHandler = null)
    {
        _catalogStore = catalogStore;
        _channelRegistry = channelRegistry;
        _reconciler = reconciler;
        _authRegistry = authRegistry;
        _broadcastLifecycle = broadcastLifecycle;
        _connectionManager = connectionManager;
        _chatPipelineHandler = chatPipelineHandler;
        _targetChannelProvider = targetChannelProvider;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _adapters = adapters.ToDictionary(static a => a.Platform);
        _monitors = monitors.ToDictionary(static m => m.Platform);

        _observedSubscription = eventBus.Subscribe<ChannelStreamObserved>(this);
    }

    public AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform)
    {
        var slot = GetSlot(platform);
        lock (slot.Sync)
        {
            return BuildPromptLocked(platform, slot);
        }
    }

    public IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts()
    {
        var list = new List<AutoDiscoverPrompt>();
        foreach (var (platform, slot) in _slots)
        {
            lock (slot.Sync)
            {
                if (BuildPromptLocked(platform, slot) is { } prompt)
                {
                    list.Add(prompt);
                }
            }
        }

        return list;
    }

    public Task<ChannelSwitchResult> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        CancellationToken cancellationToken = default)
    {
        return SwitchActiveChannelAsync(platform, targetChannel, preservePreselectedNext: false, cancellationToken);
    }

    public Task<ChannelSwitchResult> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        bool preservePreselectedNext,
        CancellationToken cancellationToken = default)
    {
        return SwitchActiveChannelAsync(platform, targetChannel, preservePreselectedNext, draftEntry: null, cancellationToken);
    }

    public Task<ChannelSwitchResult> SwitchCatalogChannelAsync(
        ChannelCatalogEntry targetChannel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetChannel);
        return SwitchActiveChannelAsync(targetChannel.Platform, targetChannel.Key, preservePreselectedNext: true,
            targetChannel, cancellationToken);
    }

    private async Task<ChannelSwitchResult> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        bool preservePreselectedNext,
        ChannelCatalogEntry? draftEntry,
        CancellationToken cancellationToken)
    {
        if (IsBlockedByDebugSession(platform))
        {
            _logger.LogWarning("Переключение канала {Platform} отключено в режиме отладки", platform);
            return new(ChannelSwitchOutcome.BlockedByDebugSession, ResolveCurrentActiveKey(platform), "Переключение канала отключено в режиме отладки.");
        }

        var slot = GetSlot(platform);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entryB = draftEntry ?? _catalogStore.FindEntry(targetChannel);
            if (entryB is null || !entryB.Key.IsValid || !entryB.IsConfigured || entryB.Platform != platform)
            {
                _logger.LogWarning("Целевой канал {Platform} {ChannelId} не найден в каталоге", platform, targetChannel.ChannelId);
                return new(ChannelSwitchOutcome.ChannelNotFound, ResolveCurrentActiveKey(platform), "Целевой канал не найден в каталоге.");
            }

            var currentActiveKey = ResolveCurrentActiveKey(platform);
            var isConnected = _connectionManager?.IsPlatformConnected(platform) ?? false;

            if (isConnected)
            {
                var preflight = await VerifyChatPrerequisitesAsync(platform, entryB, cancellationToken).ConfigureAwait(false);
                if (preflight is not null)
                {
                    await PublishSwitchFailedAsync(platform, currentActiveKey, targetChannel, keptPrevious: true, preflight).ConfigureAwait(false);
                    return new(ChannelSwitchOutcome.FailedKeptPrevious, currentActiveKey, preflight);
                }
            }

            StreamInfo? confirmedStreamB = null;
            if (_adapters.TryGetValue(platform, out var adapter))
            {
                var probe = await adapter.ProbeChannelLiveAsync(entryB, cancellationToken).ConfigureAwait(false);
                if (probe.IsLive)
                {
                    if (isConnected && !probe.IsChatReachable)
                    {
                        var reason = probe.FailureReason ?? "Чат целевого канала недоступен.";
                        await PublishSwitchFailedAsync(platform, currentActiveKey, targetChannel, keptPrevious: true, reason).ConfigureAwait(false);
                        return new(ChannelSwitchOutcome.TargetChatUnreachable, currentActiveKey, reason);
                    }

                    confirmedStreamB = probe.Stream;
                }
            }
            else if (_monitors.TryGetValue(platform, out var monitor))
            {
                var snapB = monitor.GetSnapshot(targetChannel);
                if (snapB.Status == StreamStatus.Online)
                {
                    confirmedStreamB = snapB.Stream;
                }
            }

            return await CommitSwitchLockedAsync(
                platform,
                slot,
                currentActiveKey,
                entryB,
                confirmedStreamB,
                clearPreselected: !preservePreselectedNext,
                ChannelSwitchMode.Manual,
                cancellationToken,
                saveDraftEntry: draftEntry is not null).ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async Task<ChannelSwitchResult?> SetPreselectedNextAsync(
        StreamingPlatform platform,
        ChannelKey? targetChannel,
        CancellationToken cancellationToken = default)
    {
        if (IsBlockedByDebugSession(platform))
        {
            return new(ChannelSwitchOutcome.BlockedByDebugSession, ResolveCurrentActiveKey(platform), "Предвыбор канала отключён в режиме отладки.");
        }

        var slot = GetSlot(platform);
        ChannelKey? armedTarget = null;

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentActiveKey = ResolveCurrentActiveKey(platform);
            string? nextId = null;

            if (targetChannel is { IsValid: true } key && key.Platform == platform)
            {
                var entry = _catalogStore.FindEntry(key);
                if (entry is not null && entry.IsConfigured && !string.Equals(entry.ChannelId, currentActiveKey?.ChannelId, StringComparison.Ordinal))
                {
                    nextId = entry.ChannelId;
                    armedTarget = key;
                }
            }

            _catalogStore.Mutate(settings =>
            {
                var ps = EnsurePlatformSettings(settings, platform);
                ps.PreselectedNextChannelId = nextId;
                if (nextId is not null)
                {
                    ps.SwitchMode = ChannelSwitchMode.PreselectedNext;
                }
            });

            lock (slot.Sync)
            {
                slot.FailedPreselectedNotified.Clear();
            }
        }
        finally
        {
            slot.Gate.Release();
        }

        if (armedTarget is { } armed)
        {
            return await TryExecutePreselectedNextAsync(platform, armed, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task SetSwitchModeAsync(
        StreamingPlatform platform,
        ChannelSwitchMode mode,
        CancellationToken cancellationToken = default)
    {
        var slot = GetSlot(platform);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _catalogStore.Mutate(settings =>
            {
                var ps = EnsurePlatformSettings(settings, platform);
                ps.SwitchMode = mode;
            });

            AutoDiscoverPrompt? promptToPublish = null;
            lock (slot.Sync)
            {
                slot.PendingCandidates.Clear();
                if (mode == ChannelSwitchMode.AutoDiscover && _monitors.TryGetValue(platform, out var monitor))
                {
                    foreach (var snap in monitor.GetSnapshots())
                    {
                        if (snap.Status != StreamStatus.Unknown)
                        {
                            slot.BaselineCaptured.Add(snap.Channel);
                            if (snap.Status == StreamStatus.Online)
                            {
                                var sk = new ServicedStreamKey(snap.Channel, NormalizeStreamKey(snap.Stream, snap.ObservedAtUtc));
                                slot.BaselineStreams.Add(sk);
                                slot.SeenCandidates.Add(sk);
                            }
                        }
                    }
                }

                promptToPublish = BuildPromptLocked(platform, slot);
            }

            await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, promptToPublish), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(
        StreamingPlatform platform,
        ChannelKey candidateChannel,
        CancellationToken cancellationToken = default)
    {
        if (IsBlockedByDebugSession(platform))
        {
            return new(ChannelSwitchOutcome.BlockedByDebugSession, ResolveCurrentActiveKey(platform), "Переключение канала отключено в режиме отладки.");
        }

        var slot = GetSlot(platform);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var routing = _catalogStore.GetPlatformSettings(platform);
            var entryB = _catalogStore.FindEntry(candidateChannel);
            var currentActiveKey = ResolveCurrentActiveKey(platform);
            if (entryB is null || !entryB.IsConfigured)
            {
                await RemoveCandidateAndPublishAsync(platform, slot, candidateChannel, cancellationToken).ConfigureAwait(false);
                return new(ChannelSwitchOutcome.ChannelNotFound, currentActiveKey, "Кандидат больше не настроен в каталоге.");
            }

            AutoDiscoverCandidate? pendingCandidate;
            lock (slot.Sync)
            {
                pendingCandidate = slot.PendingCandidates.FirstOrDefault(c => c.Channel == candidateChannel);
            }

            if (routing.SwitchMode != ChannelSwitchMode.AutoDiscover || pendingCandidate is null)
            {
                return new(ChannelSwitchOutcome.TargetNoLongerLive, currentActiveKey, "Предложение переключения по автопоиску уже неактуально.");
            }

            if (_adapters.TryGetValue(platform, out var adapter))
            {
                var probe = await adapter.ProbeChannelLiveAsync(entryB, cancellationToken).ConfigureAwait(false);
                if (!probe.IsLive)
                {
                    await RemoveCandidateAndPublishAsync(platform, slot, candidateChannel, cancellationToken).ConfigureAwait(false);
                    return new(ChannelSwitchOutcome.TargetNoLongerLive, currentActiveKey, probe.FailureReason ?? "Трансляция на канале уже завершилась.");
                }

                if (probe.Stream is not null && pendingCandidate.StreamKey.StartsWith("id:", StringComparison.Ordinal))
                {
                    var probedStreamKey = NormalizeStreamKey(probe.Stream, _timeProvider.GetUtcNow());
                    if (probedStreamKey.StartsWith("id:", StringComparison.Ordinal)
                        && !string.Equals(probedStreamKey, pendingCandidate.StreamKey, StringComparison.Ordinal))
                    {
                        await RemoveCandidateAndPublishAsync(platform, slot, candidateChannel, cancellationToken).ConfigureAwait(false);
                        return new(ChannelSwitchOutcome.TargetNoLongerLive, currentActiveKey, "Обнаружена уже другая трансляция – переключение отменено.");
                    }
                }

                if (!probe.IsChatReachable)
                {
                    var reason = probe.FailureReason ?? "Чат целевого канала недоступен.";
                    await PublishSwitchFailedAsync(platform, currentActiveKey, candidateChannel, keptPrevious: true, reason).ConfigureAwait(false);
                    return new(ChannelSwitchOutcome.TargetChatUnreachable, currentActiveKey, reason);
                }

                return await CommitSwitchLockedAsync(
                    platform,
                    slot,
                    currentActiveKey,
                    entryB,
                    probe.Stream,
                    clearPreselected: true,
                    ChannelSwitchMode.AutoDiscover,
                    cancellationToken).ConfigureAwait(false);
            }

            var snapB = _monitors.TryGetValue(platform, out var monitor) ? monitor.GetSnapshot(candidateChannel) : null;
            if (snapB is not { Status: StreamStatus.Online })
            {
                await RemoveCandidateAndPublishAsync(platform, slot, candidateChannel, cancellationToken).ConfigureAwait(false);
                return new(ChannelSwitchOutcome.TargetNoLongerLive, currentActiveKey, "Трансляция на канале не подтверждена.");
            }

            return await CommitSwitchLockedAsync(
                platform,
                slot,
                currentActiveKey,
                entryB,
                snapB.Stream,
                clearPreselected: true,
                ChannelSwitchMode.AutoDiscover,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async Task DismissAutoDiscoverPromptAsync(
        StreamingPlatform platform,
        CancellationToken cancellationToken = default)
    {
        var slot = GetSlot(platform);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = false;
            lock (slot.Sync)
            {
                if (slot.PendingCandidates.Count > 0)
                {
                    slot.PendingCandidates.Clear();
                    changed = true;
                }
            }

            if (changed)
            {
                await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, null), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async Task EvaluateStartupPendingAsync(
        StreamingPlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (IsBlockedByDebugSession(platform))
        {
            return;
        }

        var routing = _catalogStore.GetPlatformSettings(platform);
        if (routing.SwitchMode == ChannelSwitchMode.PreselectedNext
            && !string.IsNullOrWhiteSpace(routing.PreselectedNextChannelId))
        {
            await TryExecutePreselectedNextAsync(
                platform,
                new ChannelKey(platform, routing.PreselectedNextChannelId),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleAsync(ChannelStreamObserved @event, CancellationToken cancellationToken)
    {
        var snap = @event.Current;
        var platform = snap.Channel.Platform;
        if (IsBlockedByDebugSession(platform))
        {
            return;
        }

        var slot = GetSlot(platform);
        var routing = _catalogStore.GetPlatformSettings(platform);
        var activeKey = ResolveCurrentActiveKey(platform);
        var isActiveChannel = activeKey is { } ak && ak == snap.Channel;

        if (snap.Status == StreamStatus.Online && isActiveChannel)
        {
            lock (slot.Sync)
            {
                slot.BaselineCaptured.Add(snap.Channel);
                slot.RequireOfflineBeforeRediscover.Remove(snap.Channel);
                var sk = new ServicedStreamKey(snap.Channel, NormalizeStreamKey(snap.Stream, snap.ObservedAtUtc));
                slot.ServicedStreams.Add(sk);
                slot.SeenCandidates.Add(sk);
            }

            return;
        }

        if (snap.Status == StreamStatus.Offline)
        {
            AutoDiscoverPrompt? updatedPrompt = null;
            var promptChanged = false;

            lock (slot.Sync)
            {
                if (snap.Status != StreamStatus.Unknown)
                {
                    slot.BaselineCaptured.Add(snap.Channel);
                }

                slot.RequireOfflineBeforeRediscover.Remove(snap.Channel);
                if (slot.PendingCandidates.RemoveAll(c => c.Channel == snap.Channel) > 0)
                {
                    promptChanged = true;
                    updatedPrompt = BuildPromptLocked(platform, slot);
                }
            }

            if (promptChanged)
            {
                await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, updatedPrompt), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (snap.Status != StreamStatus.Online)
        {
            return;
        }

        // Non-active channel is Online
        if (!string.IsNullOrWhiteSpace(routing.PreselectedNextChannelId)
            && string.Equals(routing.PreselectedNextChannelId, snap.Channel.ChannelId, StringComparison.Ordinal)
            && routing.SwitchMode == ChannelSwitchMode.PreselectedNext)
        {
            await TryExecutePreselectedNextAsync(platform, snap.Channel, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (routing.SwitchMode != ChannelSwitchMode.AutoDiscover)
        {
            return;
        }

        AutoDiscoverPrompt? promptToPublish = null;
        lock (slot.Sync)
        {
            var streamKey = NormalizeStreamKey(snap.Stream, snap.ObservedAtUtc);
            var servicedKey = new ServicedStreamKey(snap.Channel, streamKey);

            if (snap.IsInitialBaseline || !slot.BaselineCaptured.Contains(snap.Channel))
            {
                slot.BaselineCaptured.Add(snap.Channel);
                slot.BaselineStreams.Add(servicedKey);
                slot.SeenCandidates.Add(servicedKey);
                return;
            }

            if (slot.RequireOfflineBeforeRediscover.Contains(snap.Channel)
                || slot.BaselineStreams.Contains(servicedKey)
                || slot.ServicedStreams.Contains(servicedKey)
                || !slot.SeenCandidates.Add(servicedKey))
            {
                return;
            }

            slot.PendingCandidates.RemoveAll(c => c.Channel == snap.Channel);
            slot.PendingCandidates.Add(new AutoDiscoverCandidate(
                snap.Channel,
                snap.ChannelDisplayName,
                streamKey,
                snap.Stream?.Title,
                snap.Stream?.StartedAt is { } dt && dt != default ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : _timeProvider.GetUtcNow(),
                _timeProvider.GetUtcNow()));
            slot.LastPromptActiveChannel = activeKey;
            promptToPublish = BuildPromptLocked(platform, slot);
        }

        if (promptToPublish is not null)
        {
            await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, promptToPublish), cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _observedSubscription.Dispose();
        foreach (var slot in _slots.Values)
        {
            slot.Gate.Dispose();
        }
    }

    private async Task<ChannelSwitchResult?> TryExecutePreselectedNextAsync(
        StreamingPlatform platform,
        ChannelKey targetChannel,
        CancellationToken cancellationToken)
    {
        var slot = GetSlot(platform);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var routing = _catalogStore.GetPlatformSettings(platform);
            if (routing.SwitchMode != ChannelSwitchMode.PreselectedNext
                || !string.Equals(routing.PreselectedNextChannelId, targetChannel.ChannelId, StringComparison.Ordinal))
            {
                return null;
            }

            var entryB = _catalogStore.FindEntry(targetChannel);
            if (entryB is null || !entryB.IsConfigured)
            {
                return null;
            }

            var currentActiveKey = ResolveCurrentActiveKey(platform);
            if (_adapters.TryGetValue(platform, out var adapter))
            {
                var probe = await adapter.ProbeChannelLiveAsync(entryB, cancellationToken).ConfigureAwait(false);
                if (!probe.IsLive)
                {
                    return null;
                }

                if (!probe.IsChatReachable)
                {
                    var streamKey = new ServicedStreamKey(targetChannel, NormalizeStreamKey(probe.Stream, _timeProvider.GetUtcNow()));
                    bool shouldNotify;
                    lock (slot.Sync)
                    {
                        shouldNotify = slot.FailedPreselectedNotified.Add(streamKey);
                    }

                    var reason = probe.FailureReason ?? $"Канал {entryB.DisplayName} в эфире, но чат недоступен.";
                    if (shouldNotify)
                    {
                        await PublishSwitchFailedAsync(platform, currentActiveKey, targetChannel, keptPrevious: true, reason).ConfigureAwait(false);
                    }

                    return new(ChannelSwitchOutcome.TargetChatUnreachable, currentActiveKey, reason);
                }

                return await CommitSwitchLockedAsync(
                    platform,
                    slot,
                    currentActiveKey,
                    entryB,
                    probe.Stream,
                    clearPreselected: true,
                    ChannelSwitchMode.PreselectedNext,
                    cancellationToken).ConfigureAwait(false);
            }

            var snap = _monitors.TryGetValue(platform, out var monitor) ? monitor.GetSnapshot(targetChannel) : null;
            if (snap is not { Status: StreamStatus.Online })
            {
                return null;
            }

            return await CommitSwitchLockedAsync(
                platform,
                slot,
                currentActiveKey,
                entryB,
                snap.Stream,
                clearPreselected: true,
                ChannelSwitchMode.PreselectedNext,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task<ChannelSwitchResult> CommitSwitchLockedAsync(
        StreamingPlatform platform,
        PlatformSwitchSlot slot,
        ChannelKey? currentActiveKey,
        ChannelCatalogEntry entryB,
        StreamInfo? confirmedStreamB,
        bool clearPreselected,
        ChannelSwitchMode mode,
        CancellationToken cancellationToken,
        bool saveDraftEntry = false)
    {
        var entryA = currentActiveKey is { IsValid: true } ak ? _catalogStore.FindEntry(ak) : null;
        var isConnected = _connectionManager?.IsPlatformConnected(platform) ?? false;
        long nextGeneration;
        var hadPrompt = false;

        lock (slot.Sync)
        {
            nextGeneration = ++slot.Generation;
            if (currentActiveKey is { IsValid: true } prevKey
                && _monitors.TryGetValue(platform, out var monitor))
            {
                var prevSnap = monitor.GetSnapshot(prevKey);
                if (prevSnap.Status == StreamStatus.Online)
                {
                    var prevStreamKey = NormalizeStreamKey(prevSnap.Stream, prevSnap.ObservedAtUtc);
                    if (string.IsNullOrEmpty(prevSnap.Stream?.Id))
                    {
                        slot.RequireOfflineBeforeRediscover.Add(prevKey);
                    }

                    var serviced = new ServicedStreamKey(prevKey, prevStreamKey);
                    slot.ServicedStreams.Add(serviced);
                    slot.SeenCandidates.Add(serviced);
                }
            }

            if (slot.PendingCandidates.Count > 0)
            {
                slot.PendingCandidates.Clear();
                hadPrompt = true;
            }
        }

        if (hadPrompt)
        {
            await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, null), cancellationToken).ConfigureAwait(false);
        }

        if (isConnected && _connectionManager is not null)
        {
            var target = new PlatformChatSessionTarget(entryB, nextGeneration, confirmedStreamB?.Id);
            try
            {
                await _connectionManager.RetargetPlatformAsync(platform, target, cancellationToken).ConfigureAwait(false);
            }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Ошибка перенацеливания чата {Platform} на канал {ChannelId}", platform, entryB.ChannelId);
                        if (entryA is not null)
                        {
                            try
                            {
                                var rollbackTarget = new PlatformChatSessionTarget(entryA, nextGeneration + 1);
                                await _connectionManager.RetargetPlatformAsync(platform, rollbackTarget, CancellationToken.None).ConfigureAwait(false);
                                var rollbackReason = $"Не удалось подключить чат {ResolveDisplayName(entryB)}: {StreamingErrorMessages.SafeMessage(ex)}";
                                await PublishSwitchFailedAsync(platform, currentActiveKey, entryB.Key, keptPrevious: true, rollbackReason).ConfigureAwait(false);
                                return new(ChannelSwitchOutcome.FailedKeptPrevious, currentActiveKey, rollbackReason);
                            }
                            catch (Exception rollbackEx)
                            {
                                _logger.LogError(rollbackEx, "Откат переключения {Platform} на прежний канал {ChannelId} не удался", platform, entryA.ChannelId);
                            }
                        }

                        await _connectionManager.MarkPlatformSwitchFailedAsync(platform, ex).ConfigureAwait(false);
                        var failReason = $"Нет активного чата на {platform} после ошибки переключения: {StreamingErrorMessages.SafeMessage(ex)}";
                        await PublishSwitchFailedAsync(platform, currentActiveKey, entryB.Key, keptPrevious: false, failReason).ConfigureAwait(false);
                        return new(ChannelSwitchOutcome.FailedNoActiveChat, null, failReason);
                    }
                }

        var previousActiveId = currentActiveKey?.ChannelId;
        var previousPreselectedId = _catalogStore.GetPlatformSettings(platform).PreselectedNextChannelId;

        ChannelCatalogEntry? previousEntryB = null;
        var catalogSaved = false;
        var catalogChangedInMemory = false;
        try
        {
            catalogSaved = _catalogStore.Mutate(settings =>
            {
                var ps = EnsurePlatformSettings(settings, platform);
                ps.ActiveChannelId = entryB.ChannelId;
                if (clearPreselected)
                {
                    ps.PreselectedNextChannelId = null;
                }

                if (saveDraftEntry)
                {
                    previousEntryB = settings.Channels.FirstOrDefault(channel => channel.Key == entryB.Key);
                    SetCatalogEntry(settings, entryB);
                }
            });
            catalogChangedInMemory = !catalogSaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Ошибка записи каталога при переключении {Platform} на канал {ChannelId}", platform, entryB.ChannelId);
        }

        if (!catalogSaved)
        {
            if (catalogChangedInMemory)
            {
                _catalogStore.Mutate(settings =>
                {
                    var ps = EnsurePlatformSettings(settings, platform);
                    ps.ActiveChannelId = previousActiveId;
                    ps.PreselectedNextChannelId = previousPreselectedId;
                    if (saveDraftEntry)
                    {
                        if (previousEntryB is null)
                        {
                            settings.Channels.RemoveAll(channel => channel.Key == entryB.Key);
                        }
                        else
                        {
                            SetCatalogEntry(settings, previousEntryB);
                        }
                    }
                });
            }

            if (isConnected && _connectionManager is not null && entryA is not null)
            {
                try
                {
                    var rollbackTarget = new PlatformChatSessionTarget(entryA, nextGeneration + 1);
                    await _connectionManager.RetargetPlatformAsync(platform, rollbackTarget, CancellationToken.None).ConfigureAwait(false);
                    _reconciler.MirrorActiveChannelToLegacySettings(entryA);
                    var rollbackReason = "Не удалось сохранить каталог каналов – переключение отменено, сохранён прежний чат.";
                    await PublishSwitchFailedAsync(platform, currentActiveKey, entryB.Key, keptPrevious: true, rollbackReason).ConfigureAwait(false);
                    return new(ChannelSwitchOutcome.FailedKeptPrevious, currentActiveKey, rollbackReason);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Откат переключения {Platform} после отказа записи каталога не удался", platform);
                    await _connectionManager.MarkPlatformSwitchFailedAsync(platform, rollbackEx).ConfigureAwait(false);
                }
            }
            else if (!isConnected && entryA is not null)
            {
                _reconciler.MirrorActiveChannelToLegacySettings(entryA);
                var savedChannelReason = "Не удалось сохранить каталог каналов – переключение отменено, сохранён прежний канал.";
                await PublishSwitchFailedAsync(platform, currentActiveKey, entryB.Key, keptPrevious: true, savedChannelReason).ConfigureAwait(false);
                return new(ChannelSwitchOutcome.FailedKeptPrevious, currentActiveKey, savedChannelReason);
            }

            var gateReason = "Не удалось сохранить каталог каналов – переключение отменено.";
            await PublishSwitchFailedAsync(platform, currentActiveKey, entryB.Key, keptPrevious: false, gateReason).ConfigureAwait(false);
            return new(ChannelSwitchOutcome.FailedNoActiveChat, currentActiveKey, gateReason);
        }

        if (confirmedStreamB is not null)
        {
            await _broadcastLifecycle.ReplaceActiveChannelAsync(platform, currentActiveKey, entryB, confirmedStreamB, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _broadcastLifecycle.LeaveActiveChannelAsync(platform, currentActiveKey, cancellationToken).ConfigureAwait(false);
        }

        _chatPipelineHandler?.ResetChannel(platform, currentActiveKey);

        var displayNameB = ResolveDisplayName(entryB);
        var labelB = !string.IsNullOrWhiteSpace(entryB.Handle) && platform == StreamingPlatform.Twitch
            ? entryB.Handle.TrimStart('@')
            : displayNameB;

        _channelRegistry.SetBinding(new(
            entryB.Key,
            labelB,
            AuthContextIds.Normalize(entryB.BotContextId),
            AuthContextIds.Normalize(entryB.BroadcasterContextId),
            nextGeneration));

        _reconciler.MirrorActiveChannelToLegacySettings(entryB);

        if (confirmedStreamB is not null)
        {
            lock (slot.Sync)
            {
                var sk = new ServicedStreamKey(entryB.Key, NormalizeStreamKey(confirmedStreamB, _timeProvider.GetUtcNow()));
                slot.ServicedStreams.Add(sk);
                slot.SeenCandidates.Add(sk);
                slot.BaselineCaptured.Add(entryB.Key);
            }
        }

        await _eventBus.PublishAsync(
            new ActiveChannelChanged(platform, currentActiveKey, entryB.Key, displayNameB, mode),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Активный канал {Platform} переключён: {From} -> {To} (режим {Mode})",
            platform,
            currentActiveKey?.ChannelId ?? "–",
            entryB.ChannelId,
            mode);

        return new(ChannelSwitchOutcome.Switched, entryB.Key);
    }

    private static void SetCatalogEntry(ChannelsSettings settings, ChannelCatalogEntry entry)
    {
        var index = settings.Channels.FindIndex(channel => channel.Key == entry.Key);
        if (index < 0)
        {
            settings.Channels.Add(entry);
        }
        else
        {
            settings.Channels[index] = entry;
        }
    }

    private async Task<string?> VerifyChatPrerequisitesAsync(
        StreamingPlatform platform,
        ChannelCatalogEntry entryB,
        CancellationToken cancellationToken)
    {
        if (!_authRegistry.TryGet(platform, out var authService))
        {
            return entryB.GetAccountAssignment(PlatformAuthRole.Bot) is { IsVerified: false }
                ? $"Подтвердите доступ аккаунта бота к каналу «{ResolveDisplayName(entryB)}»."
                : null;
        }

        if (entryB.GetAccountAssignment(PlatformAuthRole.Bot) is not { IsVerified: true })
        {
            return $"Подтвердите доступ аккаунта бота к каналу «{ResolveDisplayName(entryB)}».";
        }

        var botContextId = AuthContextIds.Normalize(entryB.BotContextId);
        var token = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return $"Нет действительной авторизации бота для канала «{ResolveDisplayName(entryB)}».";
        }

        return null;
    }

    private async Task RemoveCandidateAndPublishAsync(
        StreamingPlatform platform,
        PlatformSwitchSlot slot,
        ChannelKey candidate,
        CancellationToken ct)
    {
        AutoDiscoverPrompt? prompt = null;
        var removed = false;
        lock (slot.Sync)
        {
            if (slot.PendingCandidates.RemoveAll(c => c.Channel == candidate) > 0)
            {
                removed = true;
                prompt = BuildPromptLocked(platform, slot);
            }
        }

        if (removed)
        {
            await _eventBus.PublishAsync(new AutoDiscoverPromptChanged(platform, prompt), ct).ConfigureAwait(false);
        }
    }

    private Task PublishSwitchFailedAsync(
        StreamingPlatform platform,
        ChannelKey? fromChannel,
        ChannelKey targetChannel,
        bool keptPrevious,
        string reason)
    {
        return _eventBus.PublishAsync(
            new ChannelSwitchFailed(platform, fromChannel, targetChannel, keptPrevious, reason),
            CancellationToken.None);
    }

    private ChannelKey? ResolveCurrentActiveKey(StreamingPlatform platform)
    {
        if (_channelRegistry.GetBinding(platform) is { } binding)
        {
            return binding.Key;
        }

        var routing = _catalogStore.GetPlatformSettings(platform);
        if (!string.IsNullOrWhiteSpace(routing.ActiveChannelId))
        {
            return new ChannelKey(platform, routing.ActiveChannelId);
        }

        var configured = _catalogStore.GetConfiguredChannels(platform);
        return configured.Count > 0 ? configured[0].Key : null;
    }

    private bool IsBlockedByDebugSession(StreamingPlatform platform)
    {
        return platform == _targetChannelProvider.DebugPlatform && _targetChannelProvider.Current.IsDebugSession;
    }

    private PlatformSwitchSlot GetSlot(StreamingPlatform platform)
    {
        return _slots.GetOrAdd(platform, static _ => new PlatformSwitchSlot());
    }

    private AutoDiscoverPrompt? BuildPromptLocked(StreamingPlatform platform, PlatformSwitchSlot slot)
    {
        if (slot.PendingCandidates.Count == 0)
        {
            return null;
        }

        var active = slot.LastPromptActiveChannel ?? ResolveCurrentActiveKey(platform);
        var activeName = active is { IsValid: true } ak ? _catalogStore.ResolveDisplayName(ak) ?? ak.ChannelId : null;
        return new(platform, active, activeName, [.. slot.PendingCandidates]);
    }

    private static PlatformChannelRoutingSettings EnsurePlatformSettings(ChannelsSettings settings, StreamingPlatform platform)
    {
        var ps = settings.Platforms.FirstOrDefault(p => p.Platform == platform);
        if (ps is null)
        {
            ps = new() { Platform = platform };
            settings.Platforms.Add(ps);
        }

        return ps;
    }

    private static string ResolveDisplayName(ChannelCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(entry.Handle))
        {
            return entry.Handle;
        }

        return entry.ChannelId;
    }

    private static string NormalizeStreamKey(StreamInfo? stream, DateTimeOffset? observedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(stream?.Id))
        {
            return $"id:{stream.Id.Trim()}";
        }

        if (stream?.StartedAt is { } started && started != default)
        {
            return $"start:{DateTime.SpecifyKind(started, DateTimeKind.Utc):O}";
        }

        return observedAtUtc is { } obs ? $"obs:{obs:O}" : "unknown";
    }
}
