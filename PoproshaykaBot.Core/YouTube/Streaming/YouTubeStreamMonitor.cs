using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class YouTubeStreamMonitor(
    IYouTubeLiveBroadcastClient broadcastClient,
    YouTubeTargetChannelResolver channelResolver,
    IYouTubeBroadcastSelectionStore selectionStore,
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    YouTubeSettingsStore settingsStore,
    IYouTubeQuotaGovernor quotaGovernor,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<YouTubeStreamMonitor> logger,
    ChannelCatalogStore? channelCatalogStore = null,
    YouTubeAccountsStore? accountsStore = null,
    PlatformChannelRegistry? channelRegistry = null)
    : IStreamStatus, IPlatformChannelMonitor, IYouTubeActiveBroadcastSource, IYouTubeMonitorDiagnostics, IDisposable
{
    private static readonly TimeSpan DisabledCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OfflinePollInterval = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan OnlinePollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuotaWaitCheckCap = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan[] TransientBackoffSteps =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
    ];

    private readonly StreamStateMachine _stateMachine = new(timeProvider);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Dictionary<string, YouTubeChannelSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastDiscoveryByContext = new(StringComparer.Ordinal);

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _waitCts;
    private Task? _loopTask;
    private IDisposable? _authSubscription;
    private IDisposable? _activeChannelSubscription;

    private bool _enabled;
    private YouTubeMonitorIssue _issue;
    private string? _targetChannelId;
    private string? _lastPublishedChannelId;
    private bool _publishedOnline;
    private bool _initialStatusResolved;
    private YouTubeBroadcastSummary? _activeBroadcast;
    private string? _activeSelectionVideoId;
    private IReadOnlyList<YouTubeBroadcastSummary> _activeCandidates = [];
    private string? _lastLoggedMultiSetKey;
    private DateTimeOffset? _lastPollAt;
    private int _consecutiveFailures;
    private bool _wakeSubscriptionsAttached;
    private bool _wakePending;
    private bool _disposed;
    private StreamMonitoringStatus? _lastMonitoringStatus;
    private string? _lastMonitoringDetail;

    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public string Name => "Мониторинг эфиров YouTube";

    public int StartOrder => 256;

    public StreamStatus CurrentStatus => _stateMachine.CurrentStatus;

    public StreamInfo? CurrentStream => _stateMachine.CurrentStream;

    public YouTubeBroadcastSummary? CurrentBroadcast
    {
        get
        {
            lock (_sync)
            {
                return _activeBroadcast;
            }
        }
    }

    public YouTubeBroadcastSummary? GetActiveBroadcast(ChannelKey channel)
    {
        if (channel.Platform != StreamingPlatform.YouTube)
        {
            return null;
        }

        lock (_sync)
        {
            if (_slots.TryGetValue(channel.ChannelId, out var slot))
            {
                return slot.ActiveBroadcast;
            }

            if (string.Equals(_targetChannelId, channel.ChannelId, StringComparison.Ordinal))
            {
                return _activeBroadcast;
            }
        }

        return null;
    }

    public StreamStatusSnapshot GetSnapshot() => _stateMachine.GetSnapshot();

    public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel)
    {
        if (channel.Platform != StreamingPlatform.YouTube)
        {
            return new(channel, channel.ChannelId, ChannelMonitoringAvailability.Unavailable, MonitoringUnavailableReason.NotSupportedByPlatform, "Неверный тип площадки", StreamStatus.Unknown, null, null, false);
        }

        lock (_sync)
        {
            if (_slots.TryGetValue(channel.ChannelId, out var slot))
            {
                return slot.ToMonitoringSnapshot();
            }

            if (string.Equals(_targetChannelId, channel.ChannelId, StringComparison.Ordinal))
            {
                return new(
                    channel,
                    channel.ChannelId,
                    _issue == YouTubeMonitorIssue.None ? ChannelMonitoringAvailability.Active : ChannelMonitoringAvailability.Unavailable,
                    MapIssueToReason(_issue),
                    null,
                    _stateMachine.CurrentStatus,
                    _stateMachine.CurrentStream,
                    _lastPollAt,
                    _initialStatusResolved);
            }
        }

        var entry = channelCatalogStore?.FindEntry(channel);
        return new(
            channel,
            entry is { DisplayName.Length: > 0 } ? entry.DisplayName : channel.ChannelId,
            entry is { MonitoringEnabled: true } ? ChannelMonitoringAvailability.Unavailable : ChannelMonitoringAvailability.Disabled,
            entry is { MonitoringEnabled: true } ? MonitoringUnavailableReason.TransportDisconnected : MonitoringUnavailableReason.MonitoringDisabled,
            entry is { MonitoringEnabled: true } ? "Мониторинг не запущен" : "Мониторинг канала выключен",
            StreamStatus.Unknown,
            null,
            null,
            false);
    }

    public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots()
    {
        lock (_sync)
        {
            if (_slots.Count > 0)
            {
                return _slots.Values.Select(static s => s.ToMonitoringSnapshot()).ToArray();
            }

            if (!string.IsNullOrWhiteSpace(_targetChannelId))
            {
                var channel = new ChannelKey(StreamingPlatform.YouTube, _targetChannelId);
                return
                [
                    new(
                        channel,
                        _targetChannelId,
                        _issue == YouTubeMonitorIssue.None ? ChannelMonitoringAvailability.Active : ChannelMonitoringAvailability.Unavailable,
                        MapIssueToReason(_issue),
                        null,
                        _stateMachine.CurrentStatus,
                        _stateMachine.CurrentStream,
                        _lastPollAt,
                        _initialStatusResolved),
                ];
            }

            return [];
        }
    }

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_loopTask is not null)
            {
                return Task.CompletedTask;
            }

            _runCts = new();
            _wakePending = false;
            _authSubscription ??= eventBus.Subscribe<PlatformAuthorizationRefreshed>(OnAuthorizationRefreshed);
            _activeChannelSubscription ??= eventBus.Subscribe<ActiveChannelChanged>(OnActiveChannelChanged);
            if (!_wakeSubscriptionsAttached)
            {
                settingsStore.Changed += Wake;
                selectionStore.SelectionChanged += Wake;
                if (channelCatalogStore is not null)
                {
                    channelCatalogStore.Changed += Wake;
                }

                _wakeSubscriptionsAttached = true;
            }

            _loopTask = RunLoopAsync(_runCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource? runCts;
        Task? loopTask;
        IDisposable? authSub;
        IDisposable? activeChannelSub;
        var detachWake = false;

        lock (_sync)
        {
            runCts = _runCts;
            _runCts = null;
            loopTask = _loopTask;
            _loopTask = null;
            authSub = _authSubscription;
            _authSubscription = null;
            activeChannelSub = _activeChannelSubscription;
            _activeChannelSubscription = null;
            if (_wakeSubscriptionsAttached)
            {
                _wakeSubscriptionsAttached = false;
                detachWake = true;
            }
        }

        if (detachWake)
        {
            settingsStore.Changed -= Wake;
            selectionStore.SelectionChanged -= Wake;
            if (channelCatalogStore is not null)
            {
                channelCatalogStore.Changed -= Wake;
            }
        }

        authSub?.Dispose();
        activeChannelSub?.Dispose();

        if (runCts is not null)
        {
            await runCts.CancelAsync().ConfigureAwait(false);
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runCts?.IsCancellationRequested == true)
            {
            }
        }

        runCts?.Dispose();
        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Disconnected, null).ConfigureAwait(false);
    }

    public Task RefreshLiveSnapshotAsync()
    {
        return RefreshLiveSnapshotAsync(CancellationToken.None);
    }

    public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default)
    {
        return RefreshLiveSnapshotAsync(cancellationToken);
    }

    public async Task RefreshLiveSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!_disposed)
            {
                _refreshGate.Release();
            }
        }
    }

    public YouTubeMonitoringStatus CaptureDiagnostics()
    {
        lock (_sync)
        {
            return new(
                _enabled,
                _stateMachine.CurrentStatus,
                _issue,
                _targetChannelId,
                _activeBroadcast?.VideoId,
                _activeBroadcast?.LiveChatId,
                _activeCandidates.Count,
                _lastPollAt,
                _consecutiveFailures);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? runCts;
        CancellationTokenSource? waitCts;
        IDisposable? authSub;
        IDisposable? activeChannelSub;
        var detachWake = false;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            runCts = _runCts;
            _runCts = null;
            waitCts = _waitCts;
            authSub = _authSubscription;
            _authSubscription = null;
            activeChannelSub = _activeChannelSubscription;
            _activeChannelSubscription = null;
            if (_wakeSubscriptionsAttached)
            {
                _wakeSubscriptionsAttached = false;
                detachWake = true;
            }
        }

        if (detachWake)
        {
            settingsStore.Changed -= Wake;
            selectionStore.SelectionChanged -= Wake;
            if (channelCatalogStore is not null)
            {
                channelCatalogStore.Changed -= Wake;
            }
        }

        authSub?.Dispose();
        activeChannelSub?.Dispose();
        try { runCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { waitCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            TimeSpan nextDelay;

            try
            {
                await _refreshGate.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    lock (_sync)
                    {
                        _wakePending = false;
                    }

                    nextDelay = await PollOnceAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    if (!_disposed)
                    {
                        _refreshGate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _disposed)
            {
                break;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                break;
            }
            catch (Exception ex)
            {
                nextDelay = TransientBackoffSteps[0];
                logger.LogWarning(ex, "YouTubeStreamMonitor: непредвиденная ошибка цикла опроса");
            }

            await DelayInterruptibleAsync(nextDelay, ct).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan> PollOnceAsync(CancellationToken ct)
    {
        var settings = settingsStore.Load();

        if (!settings.Enabled)
        {
            await TransitionToDisabledAsync().ConfigureAwait(false);
            return DisabledCheckInterval;
        }

        lock (_sync)
        {
            _enabled = true;
        }

        var monitoredEntries = channelCatalogStore?.GetMonitoredChannels(StreamingPlatform.YouTube) ?? [];
        if (monitoredEntries.Count > 0)
        {
            return await PollMultiChannelAsync(settings, monitoredEntries, ct).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _slots.Clear();
        }

        var accessToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, forceRefresh: false, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await TransitionToMissingAuthAsync().ConfigureAwait(false);
            return DisabledCheckInterval;
        }

        try
        {
            var targetChannelId = await channelResolver.ResolveAsync(settings, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(targetChannelId))
            {
                await TransitionToInvalidChannelAsync().ConfigureAwait(false);
                return OfflinePollInterval;
            }

            YouTubeBroadcastSummary? active;
            string? activeSelectionVideoId;

            lock (_sync)
            {
                _targetChannelId = targetChannelId;
                active = _activeBroadcast;
                activeSelectionVideoId = _activeSelectionVideoId;
            }

            var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(targetChannelId));

            if (active is null
                || !string.Equals(active.ChannelId, targetChannelId, StringComparison.Ordinal)
                || !string.Equals(activeSelectionVideoId, selectedVideoId, StringComparison.Ordinal))
            {
                return await PollBroadcastDiscoveryAsync(targetChannelId, ct).ConfigureAwait(false);
            }

            return await PollActiveVideoSampleAsync(targetChannelId, active, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
        {
            return await HandleQuotaExceededAsync(ex.Kind == YouTubeApiErrorKind.QuotaExceeded, ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
        {
            await TransitionToMissingAuthAsync().ConfigureAwait(false);
            return HandleTransientFailure(ex, YouTubeMonitorIssue.MissingBroadcasterAuth);
        }
        catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
        {
            var retryDelay = HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
            await PublishMonitoringStatusIfChangedAsync(
                StreamMonitoringStatus.Reconnecting,
                "повтор после ошибки API").ConfigureAwait(false);
            return retryDelay;
        }
    }

    private async Task<TimeSpan> PollMultiChannelAsync(
        YouTubeSettings settings,
        IReadOnlyList<ChannelCatalogEntry> entries,
        CancellationToken ct)
    {
        var routing = channelCatalogStore!.GetPlatformSettings(StreamingPlatform.YouTube);
        var activeChannelId = channelRegistry?.GetBinding(StreamingPlatform.YouTube)?.Key.ChannelId
            ?? routing.ActiveChannelId
            ?? entries[0].ChannelId;

        var wantedIds = new HashSet<string>(entries.Select(static e => e.ChannelId), StringComparer.Ordinal);
        lock (_sync)
        {
            _targetChannelId = activeChannelId;
            foreach (var existingId in _slots.Keys.ToArray())
            {
                if (!wantedIds.Contains(existingId))
                {
                    _slots.Remove(existingId);
                }
            }

            foreach (var entry in entries)
            {
                var slot = GetOrCreateSlotLocked(entry);
                slot.UpdateMetadata(entry);
            }
        }

        var contextGroups = entries
            .GroupBy(static e => AuthContextIds.Normalize(e.BroadcasterContextId), StringComparer.Ordinal)
            .ToArray();

        var validContexts = new List<(string ContextId, List<YouTubeChannelSlot> Slots)>();
        var anyUnavailable = false;
        var anyActiveHealthy = false;
        string? firstUnavailableDetail = null;

        foreach (var group in contextGroups)
        {
            var contextId = group.Key;
            var groupSlots = group.Select(e => GetSlot(e.ChannelId)!).ToList();
            var token = AuthContextIds.IsDefault(contextId)
                ? await authService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, forceRefresh: false, ct).ConfigureAwait(false)
                : await authService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, contextId, forceRefresh: false, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(token))
            {
                anyUnavailable = true;
                firstUnavailableDetail ??= $"нет авторизации ведущего ({contextId})";
                foreach (var slot in groupSlots)
                {
                    await MarkSlotUnavailableAsync(
                        slot,
                        MonitoringUnavailableReason.MissingBroadcasterAuth,
                        "нет авторизации ведущего для канала",
                        isActive: string.Equals(slot.ChannelId, activeChannelId, StringComparison.Ordinal)).ConfigureAwait(false);
                }

                continue;
            }

            var account = accountsStore?.Load(PlatformAuthRole.Broadcaster, contextId);
            var verifiedSlots = new List<YouTubeChannelSlot>();
            foreach (var slot in groupSlots)
            {
                var unverifiedReason = CheckContextOwnershipMismatch(slot, account);
                if (unverifiedReason is not null)
                {
                    anyUnavailable = true;
                    firstUnavailableDetail ??= unverifiedReason;
                    await MarkSlotUnavailableAsync(
                        slot,
                        MonitoringUnavailableReason.AccessUnverified,
                        unverifiedReason,
                        isActive: string.Equals(slot.ChannelId, activeChannelId, StringComparison.Ordinal)).ConfigureAwait(false);
                    continue;
                }

                verifiedSlots.Add(slot);
            }

            if (verifiedSlots.Count > 0)
            {
                validContexts.Add((contextId, verifiedSlots));
            }
        }

        if (validContexts.Count == 0)
        {
            await PublishMonitoringStatusIfChangedAsync(
                StreamMonitoringStatus.Failed,
                firstUnavailableDetail ?? "нет авторизации стримера").ConfigureAwait(false);
            return DisabledCheckInterval;
        }

        var offlineInterval = ComputeEffectiveOfflineInterval(validContexts.Count, settings.DailyQuotaBudget);
        var nextDelay = offlineInterval;
        var quotaHit = false;
        Exception? lastTransientError = null;

        foreach (var (contextId, contextSlots) in validContexts)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                var activeSlot = contextSlots.FirstOrDefault(s => string.Equals(s.ChannelId, currentActiveId, StringComparison.Ordinal));
                var hasBackground = contextSlots.Any(s => !string.Equals(s.ChannelId, currentActiveId, StringComparison.Ordinal));
                var backgroundDue = hasBackground
                    && (!_lastDiscoveryByContext.TryGetValue(contextId, out var lastDiscoveryAt) || now - lastDiscoveryAt >= offlineInterval);
                var activeNeedsDiscovery = activeSlot is not null
                    && (activeSlot.ActiveBroadcast is null
                        || !string.Equals(
                            activeSlot.ActiveSelectionVideoId,
                            NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(activeSlot.ChannelId)),
                            StringComparison.Ordinal));
                var needsDiscovery = backgroundDue || activeNeedsDiscovery;

                IReadOnlyList<YouTubeBroadcastSummary> broadcasts = [];
                if (needsDiscovery)
                {
                    broadcasts = await broadcastClient.ListActiveBroadcastsAsync(contextId, ct).ConfigureAwait(false);
                    _lastDiscoveryByContext[contextId] = timeProvider.GetUtcNow();
                }

                currentActiveId = ResolveActiveChannelId(routing, entries);
                lock (_sync)
                {
                    _targetChannelId = currentActiveId;
                }

                foreach (var slot in contextSlots)
                {
                    var isActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                    if (!needsDiscovery)
                    {
                        if (isActive && slot.ActiveBroadcast is { } currentActive)
                        {
                            var delay = await PollSlotActiveVideoSampleAsync(slot, currentActive, contextId, isActive: true, ct).ConfigureAwait(false);
                            if (delay < nextDelay)
                            {
                                nextDelay = delay;
                            }

                            anyActiveHealthy = true;
                        }

                        continue;
                    }

                    var matches = broadcasts
                        .Where(b => string.Equals(b.ChannelId, slot.ChannelId, StringComparison.Ordinal))
                        .ToArray();

                    await ProcessSlotDiscoveryAsync(slot, matches, contextId, isActive, ct).ConfigureAwait(false);
                    if (isActive)
                    {
                        anyActiveHealthy = true;
                        if (slot.StateMachine.CurrentStatus == StreamStatus.Online && OnlinePollInterval < nextDelay)
                        {
                            nextDelay = OnlinePollInterval;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
            {
                quotaHit = true;
                var quotaDelay = await HandleQuotaExceededMultiChannelAsync(ex.Kind == YouTubeApiErrorKind.QuotaExceeded, activeChannelId, ct).ConfigureAwait(false);
                return quotaDelay;
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
            {
                anyUnavailable = true;
                firstUnavailableDetail ??= "нет авторизации ведущего";
                foreach (var slot in contextSlots)
                {
                    await MarkSlotUnavailableAsync(
                        slot,
                        MonitoringUnavailableReason.MissingBroadcasterAuth,
                        "токен ведущего отклонён",
                        isActive: string.Equals(slot.ChannelId, activeChannelId, StringComparison.Ordinal)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
            {
                lastTransientError = ex;
                var retryDelay = HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
                if (retryDelay < nextDelay)
                {
                    nextDelay = retryDelay;
                }
            }
        }

        if (!quotaHit)
        {
            if (lastTransientError is not null && !anyActiveHealthy)
            {
                await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Reconnecting, "повтор после ошибки API").ConfigureAwait(false);
            }
            else if (anyUnavailable)
            {
                await PublishMonitoringStatusIfChangedAsync(
                    anyActiveHealthy ? StreamMonitoringStatus.Connected : StreamMonitoringStatus.Failed,
                    anyActiveHealthy ? null : firstUnavailableDetail).ConfigureAwait(false);
            }
            else
            {
                await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);
            }
        }

        return nextDelay;
    }

    private async Task ProcessSlotDiscoveryAsync(
        YouTubeChannelSlot slot,
        YouTubeBroadcastSummary[] matches,
        string contextId,
        bool isActive,
        CancellationToken ct)
    {
        var previousSnapshot = slot.ToMonitoringSnapshot();
        var nowUtc = timeProvider.GetUtcNow();
        slot.LastPollAt = nowUtc;

        lock (_sync)
        {
            _lastPollAt = nowUtc;
            _consecutiveFailures = 0;
        }

        if (matches.Length == 0)
        {
            var transition = slot.StateMachine.ApplyOffline();
            var previousActive = slot.ActiveBroadcast;
            slot.ActiveBroadcast = null;
            slot.ActiveSelectionVideoId = null;
            slot.ActiveCandidates = [];
            slot.Availability = ChannelMonitoringAvailability.Active;
            slot.Reason = MonitoringUnavailableReason.None;
            slot.Detail = null;
            var isBaseline = !slot.BaselinePublished;
            slot.BaselinePublished = true;

            if (isActive)
            {
                var activeTransition = _stateMachine.ApplyOffline();
                lock (_sync)
                {
                    _activeBroadcast = null;
                    _activeSelectionVideoId = null;
                    _activeCandidates = [];
                    _issue = YouTubeMonitorIssue.None;
                    _lastLoggedMultiSetKey = null;
                }

                if (previousActive is not null)
                {
                    await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
                }

                await PublishOfflineIfNeededAsync(activeTransition, slot.ChannelId).ConfigureAwait(false);
            }

            if (transition.Transitioned || isBaseline || previousSnapshot.Availability != ChannelMonitoringAvailability.Active)
            {
                await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isBaseline), isCatchUp: isBaseline).ConfigureAwait(false);
            }

            return;
        }

        var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(slot.ChannelId));
        var selected = SelectBroadcast(selectedVideoId, matches);

        if (selected is null)
        {
            slot.StateMachine.ResetToUnknown();
            var previousActive = slot.ActiveBroadcast;
            slot.ActiveBroadcast = null;
            slot.ActiveSelectionVideoId = null;
            slot.ActiveCandidates = matches;
            slot.Availability = ChannelMonitoringAvailability.Degraded;
            slot.Reason = MonitoringUnavailableReason.MultipleActiveBroadcasts;
            slot.Detail = "несколько активных эфиров";

            if (isActive)
            {
                await HandleAmbiguousBroadcastsAsync(slot.ChannelId, matches, previousActive).ConfigureAwait(false);
            }

            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
            return;
        }

        slot.ActiveBroadcast = selected;
        slot.ActiveSelectionVideoId = selectedVideoId;
        slot.ActiveCandidates = matches;
        slot.Availability = ChannelMonitoringAvailability.Active;
        slot.Reason = MonitoringUnavailableReason.None;
        slot.Detail = null;

        var streamInfo = BuildStreamInfo(selected, slot.ChannelId, selected.Title, selected.StartedAtUtc.UtcDateTime, 0);
        var slotTransition = slot.StateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        var isInitial = !slot.BaselinePublished;
        slot.BaselinePublished = true;

        if (isActive)
        {
            await ActivateBroadcastAsync(slot.ChannelId, selected, matches, _activeBroadcast, selectedVideoId).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(selected.VideoId))
            {
                _ = await PollSlotActiveVideoSampleAsync(slot, selected, contextId, isActive: true, ct).ConfigureAwait(false);
            }
        }

        if (slotTransition.Transitioned || slotTransition.SnapshotChanged || isInitial || previousSnapshot.Availability != ChannelMonitoringAvailability.Active)
        {
            await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isInitial), isCatchUp: isInitial).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan> PollSlotActiveVideoSampleAsync(
        YouTubeChannelSlot slot,
        YouTubeBroadcastSummary active,
        string contextId,
        bool isActive,
        CancellationToken ct)
    {
        var previousSnapshot = slot.ToMonitoringSnapshot();
        var sample = await broadcastClient.GetVideoLiveSampleAsync(active.VideoId, contextId, ct).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow();
        slot.LastPollAt = nowUtc;

        lock (_sync)
        {
            _lastPollAt = nowUtc;
            _consecutiveFailures = 0;
        }

        if (sample is null || sample.ActualEndTimeUtc is not null)
        {
            var slotTransition = slot.StateMachine.ApplyOffline();
            slot.ActiveBroadcast = null;
            slot.ActiveSelectionVideoId = null;
            slot.ActiveCandidates = [];
            slot.Availability = ChannelMonitoringAvailability.Active;
            slot.Reason = MonitoringUnavailableReason.None;
            slot.Detail = null;
            slot.BaselinePublished = true;

            if (isActive)
            {
                var activeTransition = _stateMachine.ApplyOffline();
                lock (_sync)
                {
                    _activeBroadcast = null;
                    _activeSelectionVideoId = null;
                    _activeCandidates = [];
                    _issue = YouTubeMonitorIssue.None;
                    _lastLoggedMultiSetKey = null;
                }

                await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
                await PublishOfflineIfNeededAsync(activeTransition, slot.ChannelId).ConfigureAwait(false);
            }

            if (slotTransition.Transitioned)
            {
                await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(), isCatchUp: false).ConfigureAwait(false);
            }

            return OfflinePollInterval;
        }

        var startedAtUtc = (sample.ActualStartTimeUtc ?? active.StartedAtUtc).UtcDateTime;
        var title = string.IsNullOrWhiteSpace(sample.Title) ? active.Title : sample.Title;
        var streamInfo = BuildStreamInfo(active, slot.ChannelId, title, startedAtUtc, sample.ConcurrentViewers ?? 0);

        StatusTransition slotOnlineTransition;
        if (sample.ConcurrentViewers.HasValue)
        {
            slotOnlineTransition = slot.StateMachine.ApplyOnlineSnapshot(streamInfo);
        }
        else
        {
            slotOnlineTransition = slot.StateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        }

        slot.Availability = ChannelMonitoringAvailability.Active;
        slot.Reason = MonitoringUnavailableReason.None;
        slot.Detail = null;
        var isBaseline = !slot.BaselinePublished;
        slot.BaselinePublished = true;

        if (isActive)
        {
            StatusTransition activeTransition;
            if (sample.ConcurrentViewers.HasValue)
            {
                activeTransition = _stateMachine.ApplyOnlineSnapshot(streamInfo);
                await eventBus.PublishAsync(
                    new ViewerSampleObserved(
                        StreamingPlatform.YouTube,
                        slot.ChannelId,
                        sample.ConcurrentViewers.Value,
                        nowUtc,
                        active.VideoId,
                        slot.ChannelId),
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                activeTransition = _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
            }

            lock (_sync)
            {
                _issue = YouTubeMonitorIssue.None;
            }

            await PublishOnlineOrMetadataAsync(activeTransition, slot.ChannelId).ConfigureAwait(false);
        }

        if (slotOnlineTransition.Transitioned || slotOnlineTransition.SnapshotChanged || isBaseline)
        {
            await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isBaseline), isCatchUp: isBaseline).ConfigureAwait(false);
        }

        return OnlinePollInterval;
    }

    private async Task MarkSlotUnavailableAsync(
        YouTubeChannelSlot slot,
        MonitoringUnavailableReason reason,
        string detail,
        bool isActive)
    {
        var previous = slot.ToMonitoringSnapshot();
        slot.StateMachine.ResetToUnknown();
        slot.ActiveBroadcast = null;
        slot.ActiveSelectionVideoId = null;
        slot.ActiveCandidates = [];
        slot.Availability = ChannelMonitoringAvailability.Unavailable;
        slot.Reason = reason;
        slot.Detail = detail;

        if (isActive)
        {
            _stateMachine.ResetToUnknown();
            YouTubeBroadcastSummary? previousActive;
            lock (_sync)
            {
                _issue = reason == MonitoringUnavailableReason.QuotaExhausted
                    ? YouTubeMonitorIssue.QuotaExhausted
                    : YouTubeMonitorIssue.MissingBroadcasterAuth;
                previousActive = _activeBroadcast;
                _activeBroadcast = null;
                _activeSelectionVideoId = null;
                _activeCandidates = [];
                _lastLoggedMultiSetKey = null;
            }

            if (previousActive is not null)
            {
                await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (previous.Availability != slot.Availability
            || previous.Reason != slot.Reason
            || !string.Equals(previous.Detail, slot.Detail, StringComparison.Ordinal))
        {
            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan> HandleQuotaExceededMultiChannelAsync(
        bool isServerExhausted,
        string activeChannelId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (isServerExhausted)
        {
            quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Broadcaster);
        }

        YouTubeChannelSlot[] slotsCopy;
        lock (_sync)
        {
            slotsCopy = [.. _slots.Values];
        }

        foreach (var slot in slotsCopy)
        {
            await MarkSlotUnavailableAsync(
                slot,
                MonitoringUnavailableReason.QuotaExhausted,
                "квота исчерпана",
                isActive: string.Equals(slot.ChannelId, activeChannelId, StringComparison.Ordinal)).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "квота исчерпана").ConfigureAwait(false);

        var nowUtc = timeProvider.GetUtcNow();
        var window = PacificQuotaClock.GetWindow(nowUtc);
        var remaining = window.NextResetAtUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(5);
        }

        var cap = isServerExhausted ? QuotaWaitCheckCap : OfflinePollInterval;
        return remaining > cap ? cap : remaining;
    }

    private static string? CheckContextOwnershipMismatch(YouTubeChannelSlot slot, Auth.YouTubeAccountSettings? account)
    {
        var assignment = slot.Entry.GetAccountAssignment(PlatformAuthRole.Broadcaster);
        if (assignment is { IsVerified: false })
        {
            return "доступ аккаунта ведущего к каналу YouTube не подтверждён";
        }

        if (account is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(account.ChannelId)
            && !string.Equals(account.ChannelId, slot.ChannelId, StringComparison.Ordinal))
        {
            return "выбранный аккаунт ведущего принадлежит другому каналу YouTube";
        }

        if (assignment is not null
            && !string.Equals(assignment.Account.AccountId, account.ChannelId, StringComparison.Ordinal))
        {
            return "авторизация ведущего изменилась – требуется повторная проверка доступа";
        }

        return null;
    }

    private static TimeSpan ComputeEffectiveOfflineInterval(int validContextCount, int dailyQuotaBudget)
    {
        var usableDailyUnits = Math.Max(1.0, dailyQuotaBudget * 0.35);
        var secondsPerCheck = (int)Math.Ceiling(Math.Max(1, validContextCount) * 86400.0 / usableDailyUnits);
        return TimeSpan.FromSeconds(Math.Clamp(secondsPerCheck, (int)OfflinePollInterval.TotalSeconds, (int)QuotaWaitCheckCap.TotalSeconds));
    }

    private Task PublishSlotObservedAsync(ChannelMonitoringSnapshot previous, ChannelMonitoringSnapshot current, bool isCatchUp)
    {
        return eventBus.PublishAsync(new ChannelStreamObserved(previous, current, isCatchUp), CancellationToken.None);
    }

    private Task PublishSlotStateChangedAsync(YouTubeChannelSlot slot)
    {
        return eventBus.PublishAsync(
            new ChannelMonitoringStateChanged(StreamingPlatform.YouTube, new ChannelKey(StreamingPlatform.YouTube, slot.ChannelId), slot.ToMonitoringSnapshot()),
            CancellationToken.None);
    }

    private YouTubeChannelSlot GetOrCreateSlotLocked(ChannelCatalogEntry entry)
    {
        if (!_slots.TryGetValue(entry.ChannelId, out var slot))
        {
            slot = new(entry, timeProvider);
            _slots[entry.ChannelId] = slot;
        }

        return slot;
    }

    private YouTubeChannelSlot? GetSlot(string channelId)
    {
        lock (_sync)
        {
            return _slots.TryGetValue(channelId, out var slot) ? slot : null;
        }
    }

    private void OnActiveChannelChanged(ActiveChannelChanged @event)
    {
        if (@event.Platform != StreamingPlatform.YouTube)
        {
            return;
        }

        lock (_sync)
        {
            _targetChannelId = @event.NewChannel.ChannelId;
            _activeBroadcast = _slots.TryGetValue(@event.NewChannel.ChannelId, out var slot)
                ? slot.ActiveBroadcast
                : null;
            _stateMachine.ResetToUnknown();
            if (_slots.TryGetValue(@event.NewChannel.ChannelId, out var newSlot) && newSlot.StateMachine.CurrentStream is { } stream)
            {
                _stateMachine.ApplyOnlineWithoutViewerSample(stream);
                _publishedOnline = true;
                _lastPublishedChannelId = @event.NewChannel.ChannelId;
            }
            else
            {
                _publishedOnline = false;
                _lastPublishedChannelId = null;
            }
        }

        Wake();
    }

    private async Task<TimeSpan> PollBroadcastDiscoveryAsync(string targetChannelId, CancellationToken ct)
    {
        var allBroadcasts = await broadcastClient.ListActiveBroadcastsAsync(ct).ConfigureAwait(false);
        var matches = allBroadcasts
            .Where(b => string.Equals(b.ChannelId, targetChannelId, StringComparison.Ordinal))
            .ToArray();

        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            _lastPollAt = timeProvider.GetUtcNow();
            _consecutiveFailures = 0;
            previousActive = _activeBroadcast;
        }

        if (matches.Length == 0)
        {
            await HandleNoActiveBroadcastsAsync(targetChannelId, previousActive).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(targetChannelId));
        var selected = SelectBroadcast(selectedVideoId, matches);

        if (selected is null)
        {
            await HandleAmbiguousBroadcastsAsync(targetChannelId, matches, previousActive).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        await ActivateBroadcastAsync(targetChannelId, selected, matches, previousActive, selectedVideoId).ConfigureAwait(false);
        return OnlinePollInterval;
    }

    private async Task<TimeSpan> PollActiveVideoSampleAsync(
        string targetChannelId,
        YouTubeBroadcastSummary active,
        CancellationToken ct)
    {
        var sample = await broadcastClient.GetVideoLiveSampleAsync(active.VideoId, ct).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow();

        lock (_sync)
        {
            _lastPollAt = nowUtc;
            _consecutiveFailures = 0;
        }

        if (sample is null || sample.ActualEndTimeUtc is not null)
        {
            var transition = _stateMachine.ApplyOffline();

            lock (_sync)
            {
                _activeBroadcast = null;
                _activeSelectionVideoId = null;
                _activeCandidates = [];
                _issue = YouTubeMonitorIssue.None;
                _lastLoggedMultiSetKey = null;
            }

            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);

            await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);
            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);

            return OfflinePollInterval;
        }

        var startedAtUtc = (sample.ActualStartTimeUtc ?? active.StartedAtUtc).UtcDateTime;
        var title = string.IsNullOrWhiteSpace(sample.Title) ? active.Title : sample.Title;
        var streamInfo = BuildStreamInfo(active, targetChannelId, title, startedAtUtc, sample.ConcurrentViewers ?? 0);

        StatusTransition onlineTransition;

        if (sample.ConcurrentViewers.HasValue)
        {
            onlineTransition = _stateMachine.ApplyOnlineSnapshot(streamInfo);
            await eventBus.PublishAsync(
                new ViewerSampleObserved(
                    StreamingPlatform.YouTube,
                    targetChannelId,
                    sample.ConcurrentViewers.Value,
                    nowUtc,
                    active.VideoId,
                    targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            onlineTransition = _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        }

        lock (_sync)
        {
            _issue = YouTubeMonitorIssue.None;
        }

        await PublishOnlineOrMetadataAsync(onlineTransition, targetChannelId).ConfigureAwait(false);
        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);

        return OnlinePollInterval;
    }

    private static YouTubeBroadcastSummary? SelectBroadcast(
        string? selectedVideoId,
        YouTubeBroadcastSummary[] matches)
    {
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (!string.IsNullOrWhiteSpace(selectedVideoId))
        {
            return matches.FirstOrDefault(m => string.Equals(m.VideoId, selectedVideoId, StringComparison.Ordinal));
        }

        return null;
    }

    private static string? NormalizeSelectedVideoId(string? videoId)
    {
        return string.IsNullOrWhiteSpace(videoId) ? null : videoId.Trim();
    }

    private async Task ActivateBroadcastAsync(
        string targetChannelId,
        YouTubeBroadcastSummary selected,
        YouTubeBroadcastSummary[] matches,
        YouTubeBroadcastSummary? previousActive,
        string? selectedVideoId)
    {
        var switchedBroadcast = previousActive is not null
            && (!string.Equals(previousActive.VideoId, selected.VideoId, StringComparison.Ordinal)
                || !string.Equals(previousActive.ChannelId, targetChannelId, StringComparison.Ordinal)
                || !string.Equals(previousActive.LiveChatId, selected.LiveChatId, StringComparison.Ordinal));

        if (!switchedBroadcast && previousActive is not null && _stateMachine.CurrentStatus == StreamStatus.Online)
        {
            lock (_sync)
            {
                _activeBroadcast = selected;
                _activeSelectionVideoId = selectedVideoId;
                _activeCandidates = matches;
                _issue = YouTubeMonitorIssue.None;
                _lastLoggedMultiSetKey = null;
            }

            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);
            return;
        }

        var hadActiveBroadcast = previousActive is not null;

        lock (_sync)
        {
            _activeBroadcast = null;
            _activeSelectionVideoId = selectedVideoId;
            _activeCandidates = matches;
            _issue = YouTubeMonitorIssue.None;
            _lastLoggedMultiSetKey = null;
        }

        if (hadActiveBroadcast)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        if (switchedBroadcast)
        {
            _stateMachine.ResetToUnknown();
        }

        var streamInfo = BuildStreamInfo(selected, targetChannelId, selected.Title, selected.StartedAtUtc.UtcDateTime, 0);
        var transition = _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);

        await PublishOnlineOrMetadataAsync(transition, targetChannelId).ConfigureAwait(false);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _activeBroadcast = selected;
        }

        await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(selected), CancellationToken.None).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(selected.LiveChatId))
        {
            await eventBus.PublishAsync(
                new PlatformChatAvailabilityChanged(StreamingPlatform.YouTube, true),
                CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);
    }

    private async Task HandleNoActiveBroadcastsAsync(string targetChannelId, YouTubeBroadcastSummary? previousActive)
    {
        var transition = _stateMachine.ApplyOffline();

        lock (_sync)
        {
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = [];
            _issue = YouTubeMonitorIssue.None;
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);
        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null).ConfigureAwait(false);
    }

    private async Task HandleAmbiguousBroadcastsAsync(
        string targetChannelId,
        YouTubeBroadcastSummary[] matches,
        YouTubeBroadcastSummary? previousActive)
    {
        _stateMachine.ResetToUnknown();

        var setKey = string.Join(",", matches.Select(static m => m.VideoId).OrderBy(static id => id, StringComparer.Ordinal));
        var shouldLogWarning = false;

        lock (_sync)
        {
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = matches;
            _issue = YouTubeMonitorIssue.MultipleActiveBroadcasts;

            if (!string.Equals(_lastLoggedMultiSetKey, setKey, StringComparison.Ordinal))
            {
                _lastLoggedMultiSetKey = setKey;
                shouldLogWarning = true;
            }
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        if (shouldLogWarning)
        {
            logger.LogWarning(
                "YouTube: на канале {ChannelId} одновременно найдено {Count} активных трансляций ({VideoIds}). Автоматический выбор отключён",
                targetChannelId,
                matches.Length,
                setKey);
        }

        await eventBus.PublishAsync(
            new YouTubeMultipleBroadcastsDetected(targetChannelId, matches),
            CancellationToken.None).ConfigureAwait(false);

        await PublishMonitoringStatusIfChangedAsync(
            StreamMonitoringStatus.Reconnecting,
            "несколько активных эфиров").ConfigureAwait(false);
    }

    private async Task TransitionToDisabledAsync()
    {
        var previousStatus = _stateMachine.CurrentStatus;
        _stateMachine.ResetToUnknown();
        YouTubeBroadcastSummary? previousActive;
        bool wasPublishedOnline;
        string offlineChannelId;

        lock (_sync)
        {
            _enabled = false;
            _issue = YouTubeMonitorIssue.None;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
            _consecutiveFailures = 0;
            wasPublishedOnline = previousStatus == StreamStatus.Online || _publishedOnline;
            offlineChannelId = _lastPublishedChannelId ?? _targetChannelId ?? string.Empty;
            if (wasPublishedOnline)
            {
                _publishedOnline = false;
                _lastPublishedChannelId = null;
            }

            foreach (var slot in _slots.Values)
            {
                slot.StateMachine.ResetToUnknown();
                slot.ActiveBroadcast = null;
                slot.Availability = ChannelMonitoringAvailability.Disabled;
                slot.Reason = MonitoringUnavailableReason.MonitoringDisabled;
                slot.Detail = null;
            }
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        if (wasPublishedOnline)
        {
            await eventBus.PublishAsync(
                new StreamWentOffline(
                    StreamingPlatform.YouTube,
                    offlineChannelId,
                    IsCatchUp: previousStatus == StreamStatus.Unknown),
                CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Disconnected, null).ConfigureAwait(false);
    }

    private async Task TransitionToMissingAuthAsync()
    {
        _stateMachine.ResetToUnknown();
        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            _issue = YouTubeMonitorIssue.MissingBroadcasterAuth;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "нет авторизации стримера").ConfigureAwait(false);
    }

    private async Task TransitionToInvalidChannelAsync()
    {
        _stateMachine.ResetToUnknown();
        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            _targetChannelId = null;
            _issue = YouTubeMonitorIssue.InvalidTargetChannel;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "неверный канал").ConfigureAwait(false);
    }

    private async Task<TimeSpan> HandleQuotaExceededAsync(bool isServerExhausted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (isServerExhausted)
        {
            quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Broadcaster);
        }

        _stateMachine.ResetToUnknown();

        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            _issue = YouTubeMonitorIssue.QuotaExhausted;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "квота исчерпана").ConfigureAwait(false);

        var nowUtc = timeProvider.GetUtcNow();
        var window = PacificQuotaClock.GetWindow(nowUtc);
        var remaining = window.NextResetAtUtc - nowUtc;

        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(5);
        }

        var cap = isServerExhausted ? QuotaWaitCheckCap : OfflinePollInterval;
        return remaining > cap ? cap : remaining;
    }

    private async Task PublishOnlineOrMetadataAsync(StatusTransition transition, string targetChannelId)
    {
        if (transition.Transitioned)
        {
            lock (_sync)
            {
                _publishedOnline = true;
                _lastPublishedChannelId = targetChannelId;
                _initialStatusResolved = true;
            }

            await eventBus.PublishAsync(
                new StreamWentOnline(StreamingPlatform.YouTube, targetChannelId, _stateMachine.CurrentStream, IsCatchUp: false, ChannelId: targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }
        else if (transition.SnapshotChanged && _stateMachine.CurrentStream is { } updatedStream)
        {
            await eventBus.PublishAsync(
                new StreamMetadataResolved(StreamingPlatform.YouTube, targetChannelId, updatedStream, ChannelId: targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PublishOfflineIfNeededAsync(StatusTransition transition, string targetChannelId)
    {
        var isCatchUp = transition.Previous == StreamStatus.Unknown;
        bool shouldPublish;

        lock (_sync)
        {
            shouldPublish = transition.Transitioned
                && (transition.Previous == StreamStatus.Online || _publishedOnline || !_initialStatusResolved);
            _initialStatusResolved = true;
            if (shouldPublish)
            {
                _publishedOnline = false;
                _lastPublishedChannelId = null;
            }
        }

        if (shouldPublish)
        {
            await eventBus.PublishAsync(
                new StreamWentOffline(StreamingPlatform.YouTube, targetChannelId, IsCatchUp: isCatchUp, ChannelId: targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private TimeSpan HandleTransientFailure(Exception exception, YouTubeMonitorIssue issue)
    {
        int failures;

        lock (_sync)
        {
            _consecutiveFailures++;
            failures = _consecutiveFailures;
            _issue = issue;
        }

        var index = Math.Clamp(failures - 1, 0, TransientBackoffSteps.Length - 1);
        var delay = TransientBackoffSteps[index];

        logger.LogWarning(
            exception,
            "YouTubeStreamMonitor: сбой опроса ({Issue}, попытка #{Failures}), повтор через {DelaySeconds} с",
            issue,
            failures,
            (int)delay.TotalSeconds);

        return delay;
    }

    private void OnAuthorizationRefreshed(PlatformAuthorizationRefreshed @event)
    {
        if (@event.Platform == StreamingPlatform.YouTube && @event.Role == PlatformAuthRole.Broadcaster)
        {
            Wake();
        }
    }

    private void Wake(object? sender, EventArgs e)
    {
        Wake();
    }

    private void Wake()
    {
        CancellationTokenSource? waitCts;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _wakePending = true;
            waitCts = _waitCts;
        }

        if (waitCts is null)
        {
            return;
        }

        try
        {
            waitCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task DelayInterruptibleAsync(TimeSpan delay, CancellationToken ct)
    {
        CancellationTokenSource? waitCts = null;

        try
        {
            lock (_sync)
            {
                if (_disposed || ct.IsCancellationRequested)
                {
                    return;
                }

                if (_wakePending)
                {
                    _wakePending = false;
                    return;
                }

                _waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                waitCts = _waitCts;
            }

            await Task.Delay(delay, timeProvider, waitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        finally
        {
            if (waitCts is not null)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_waitCts, waitCts))
                    {
                        _waitCts = null;
                    }
                }

                waitCts.Dispose();
            }
        }
    }

    private Task PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus status, string? detail)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_lastMonitoringStatus == status && string.Equals(_lastMonitoringDetail, detail, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            _lastMonitoringStatus = status;
            _lastMonitoringDetail = detail;
        }

        logger.LogDebug("YouTube: публикация StreamMonitoringStatusChanged {Status} ({Detail})", status, detail ?? "–");
        return eventBus.PublishAsync(
            new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster, status, detail),
            CancellationToken.None);
    }

    private static MonitoringUnavailableReason MapIssueToReason(YouTubeMonitorIssue issue)
    {
        return issue switch
        {
            YouTubeMonitorIssue.MissingBroadcasterAuth => MonitoringUnavailableReason.MissingBroadcasterAuth,
            YouTubeMonitorIssue.InvalidTargetChannel => MonitoringUnavailableReason.InvalidChannel,
            YouTubeMonitorIssue.QuotaExhausted => MonitoringUnavailableReason.QuotaExhausted,
            YouTubeMonitorIssue.MultipleActiveBroadcasts => MonitoringUnavailableReason.MultipleActiveBroadcasts,
            YouTubeMonitorIssue.TransientError => MonitoringUnavailableReason.TransportDisconnected,
            _ => MonitoringUnavailableReason.None,
        };
    }

    private static StreamInfo BuildStreamInfo(
        YouTubeBroadcastSummary broadcast,
        string targetChannelId,
        string title,
        DateTime startedAtUtc,
        int viewerCount)
    {
        var thumbnailUrl = string.IsNullOrWhiteSpace(broadcast.VideoId)
            ? string.Empty
            : $"https://i.ytimg.com/vi/{Uri.EscapeDataString(broadcast.VideoId)}/hqdefault.jpg";

        return new()
        {
            Id = broadcast.VideoId,
            UserId = broadcast.ChannelId,
            UserLogin = targetChannelId,
            UserName = broadcast.ChannelTitle,
            Title = title,
            GameName = string.Empty,
            ViewerCount = viewerCount,
            StartedAt = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc),
            ThumbnailUrl = thumbnailUrl,
        };
    }

    private sealed class YouTubeChannelSlot(ChannelCatalogEntry entry, TimeProvider timeProvider)
    {
        public string ChannelId { get; } = entry.ChannelId;

        public ChannelCatalogEntry Entry { get; private set; } = entry;

        public StreamStateMachine StateMachine { get; } = new(timeProvider);

        public YouTubeBroadcastSummary? ActiveBroadcast { get; set; }

        public string? ActiveSelectionVideoId { get; set; }

        public IReadOnlyList<YouTubeBroadcastSummary> ActiveCandidates { get; set; } = [];

        public ChannelMonitoringAvailability Availability { get; set; } = ChannelMonitoringAvailability.Active;

        public MonitoringUnavailableReason Reason { get; set; }

        public string? Detail { get; set; }

        public DateTimeOffset? LastPollAt { get; set; }

        public bool BaselinePublished { get; set; }

        public void UpdateMetadata(ChannelCatalogEntry entry)
        {
            Entry = entry;
        }

        public ChannelMonitoringSnapshot ToMonitoringSnapshot(bool isInitialBaseline = false)
        {
            var label = !string.IsNullOrWhiteSpace(Entry.DisplayName)
                ? Entry.DisplayName
                : (!string.IsNullOrWhiteSpace(Entry.Handle) ? Entry.Handle : ChannelId);

            return new(
                new ChannelKey(StreamingPlatform.YouTube, ChannelId),
                label,
                Availability,
                Reason,
                Detail,
                StateMachine.CurrentStatus,
                StateMachine.CurrentStream,
                LastPollAt,
                isInitialBaseline);
        }
    }

    private string ResolveActiveChannelId(PlatformChannelRoutingSettings routing, IReadOnlyList<ChannelCatalogEntry> entries)
    {
        return channelRegistry?.GetBinding(StreamingPlatform.YouTube)?.Key.ChannelId
            ?? channelCatalogStore?.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId
            ?? routing.ActiveChannelId
            ?? entries[0].ChannelId;
    }
}
