using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class YouTubeStreamMonitor
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

    private readonly IYouTubeLiveBroadcastClient broadcastClient;
    private readonly YouTubeTargetChannelResolver channelResolver;
    private readonly IYouTubeBroadcastSelectionStore selectionStore;
    private readonly IPlatformAuthService authService;
    private readonly YouTubeSettingsStore settingsStore;
    private readonly IYouTubeQuotaGovernor quotaGovernor;
    private readonly IEventBus eventBus;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<YouTubeStreamMonitor> logger;
    private readonly ChannelCatalogStore? channelCatalogStore;
    private readonly YouTubeAccountsStore? accountsStore;
    private readonly PlatformChannelRegistry? channelRegistry;
    private readonly IYouTubePublicLiveDiscoveryClient? publicDiscovery;

    private readonly StreamStateMachine _stateMachine;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Dictionary<string, YouTubeChannelSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastDiscoveryByContext = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastPublicDiscoveryByChannel = new(StringComparer.Ordinal);

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _waitCts;
    private Task? _loopTask;
    private IDisposable? _authSubscription;
    private IDisposable? _activeChannelSubscription;

    private long _settingsRevision;
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
    private PlatformAuthRole? _lastMonitoringRole;
    private PlatformAuthRole? _activeDiscoveryRole;
    private YouTubeMonitoringTransport _transport;
    private StreamMonitoringStatus? _lastMonitoringStatus;
    private string? _lastMonitoringDetail;

    public YouTubeStreamMonitor(
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
        PlatformChannelRegistry? channelRegistry = null,
        IYouTubePublicLiveDiscoveryClient? publicDiscovery = null)
    {
        this.broadcastClient = broadcastClient;
        this.channelResolver = channelResolver;
        this.selectionStore = selectionStore;
        this.authService = authService;
        this.settingsStore = settingsStore;
        this.quotaGovernor = quotaGovernor;
        this.eventBus = eventBus;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.channelCatalogStore = channelCatalogStore;
        this.accountsStore = accountsStore;
        this.channelRegistry = channelRegistry;
        this.publicDiscovery = publicDiscovery;
        _stateMachine = new(timeProvider);
        this.settingsStore.Changed += OnSettingsChanged;
    }

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
        PlatformAuthRole disconnectRole;
        lock (_sync)
        {
            disconnectRole = _lastMonitoringRole ?? PlatformAuthRole.Broadcaster;
            _transport = YouTubeMonitoringTransport.None;
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Disconnected, null, disconnectRole).ConfigureAwait(false);
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
            await PollOnceAsync(forceActiveSample: true, cancellationToken).ConfigureAwait(false);
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
                _consecutiveFailures,
                _transport);
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

        settingsStore.Changed -= OnSettingsChanged;

        if (detachWake)
        {
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

                    nextDelay = await PollOnceAsync(forceActiveSample: false, ct).ConfigureAwait(false);
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

    private async Task<TimeSpan> PollOnceAsync(bool forceActiveSample, CancellationToken ct)
    {
        var settingsRevision = Volatile.Read(ref _settingsRevision);
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
            return await PollMultiChannelAsync(settings, monitoredEntries, forceActiveSample, settingsRevision, ct).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _slots.Clear();
            _lastPublicDiscoveryByChannel.Clear();
        }

        var broadcasterToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, forceRefresh: false, ct)
            .ConfigureAwait(false);
        var botToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, forceRefresh: false, ct)
            .ConfigureAwait(false);

        if (Volatile.Read(ref _settingsRevision) != settingsRevision)
        {
            return TimeSpan.Zero;
        }

        if (string.IsNullOrWhiteSpace(broadcasterToken) && string.IsNullOrWhiteSpace(botToken))
        {
            PlatformAuthRole missingRole;
            lock (_sync)
            {
                missingRole = _lastMonitoringRole == PlatformAuthRole.Bot ? PlatformAuthRole.Bot : PlatformAuthRole.Broadcaster;
            }

            await TransitionToMissingAuthAsync(missingRole, settingsRevision).ConfigureAwait(false);
            return DisabledCheckInterval;
        }

        var resolveRole = !string.IsNullOrWhiteSpace(broadcasterToken)
            ? PlatformAuthRole.Broadcaster
            : PlatformAuthRole.Bot;
        var activeRole = resolveRole;

        try
        {
            string? targetChannelId;
            if (resolveRole == PlatformAuthRole.Broadcaster)
            {
                try
                {
                    targetChannelId = await channelResolver.ResolveAsync(settings, ct).ConfigureAwait(false);
                }
                catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized && !string.IsNullOrWhiteSpace(botToken))
                {
                    logger.LogDebug(ex, "YouTubeStreamMonitor: токен ведущего вернул 401 при определении канала, переключаемся на бота");
                    resolveRole = PlatformAuthRole.Bot;
                    activeRole = PlatformAuthRole.Bot;
                    targetChannelId = await channelResolver.ResolveAsync(settings, PlatformAuthRole.Bot, null, ct).ConfigureAwait(false);
                }
            }
            else
            {
                targetChannelId = await channelResolver.ResolveAsync(settings, PlatformAuthRole.Bot, null, ct).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _settingsRevision) != settingsRevision)
            {
                return TimeSpan.Zero;
            }

            if (string.IsNullOrWhiteSpace(targetChannelId))
            {
                await TransitionToInvalidChannelAsync(resolveRole, settingsRevision).ConfigureAwait(false);
                return OfflinePollInterval;
            }

            var broadcasterChannelId = accountsStore?.Load(PlatformAuthRole.Broadcaster).ChannelId?.Trim();
            var hasUsableBot = !string.IsNullOrWhiteSpace(botToken) && publicDiscovery is not null;
            var broadcasterConfirmed = !string.IsNullOrWhiteSpace(broadcasterChannelId)
                ? string.Equals(broadcasterChannelId, targetChannelId, StringComparison.Ordinal)
                : !hasUsableBot;
            var useBroadcaster = !string.IsNullOrWhiteSpace(broadcasterToken) && broadcasterConfirmed;

            if (!useBroadcaster && !hasUsableBot)
            {
                var missingRole = string.IsNullOrWhiteSpace(broadcasterToken) && !string.IsNullOrWhiteSpace(botToken)
                    ? PlatformAuthRole.Bot
                    : PlatformAuthRole.Broadcaster;
                await TransitionToMissingAuthAsync(missingRole, settingsRevision).ConfigureAwait(false);
                return DisabledCheckInterval;
            }

            activeRole = useBroadcaster ? PlatformAuthRole.Broadcaster : PlatformAuthRole.Bot;

            YouTubeBroadcastSummary? active;
            string? activeSelectionVideoId;
            PlatformAuthRole? activeDiscoveryRole;

            lock (_sync)
            {
                if (_disposed || Volatile.Read(ref _settingsRevision) != settingsRevision)
                {
                    return TimeSpan.Zero;
                }

                _targetChannelId = targetChannelId;
                active = _activeBroadcast;
                activeSelectionVideoId = _activeSelectionVideoId;
                activeDiscoveryRole = _activeDiscoveryRole;
            }

            var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(targetChannelId));

            if (useBroadcaster)
            {
                try
                {
                    if (active is null
                        || !string.Equals(active.ChannelId, targetChannelId, StringComparison.Ordinal)
                        || !string.Equals(activeSelectionVideoId, selectedVideoId, StringComparison.Ordinal)
                        || activeDiscoveryRole != PlatformAuthRole.Broadcaster)
                    {
                        return await PollBroadcastDiscoveryAsync(targetChannelId, settingsRevision, ct).ConfigureAwait(false);
                    }

                    return await PollActiveVideoSampleAsync(targetChannelId, active, PlatformAuthRole.Broadcaster, settingsRevision, ct).ConfigureAwait(false);
                }
                catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized && hasUsableBot)
                {
                    logger.LogDebug(ex, "YouTubeStreamMonitor: токен ведущего вернул 401 при опросе {ChannelId}, переключаемся на бота", targetChannelId);
                    activeRole = PlatformAuthRole.Bot;
                }
            }

            if (active is null
                || !string.Equals(active.ChannelId, targetChannelId, StringComparison.Ordinal)
                || !string.Equals(activeSelectionVideoId, selectedVideoId, StringComparison.Ordinal)
                || activeDiscoveryRole != PlatformAuthRole.Bot)
            {
                return await PollBotOnlyDiscoveryAsync(targetChannelId, selectedVideoId, settingsRevision, ct).ConfigureAwait(false);
            }

            return await PollActiveVideoSampleAsync(targetChannelId, active, PlatformAuthRole.Bot, settingsRevision, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
        {
            return await HandleQuotaExceededAsync(
                ex.Kind == YouTubeApiErrorKind.QuotaExceeded,
                ex.QuotaExhaustionRecorded,
                activeRole,
                settingsRevision,
                ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
        {
            if (Volatile.Read(ref _settingsRevision) != settingsRevision)
            {
                return TimeSpan.Zero;
            }

            await TransitionToMissingAuthAsync(activeRole, settingsRevision).ConfigureAwait(false);
            return HandleTransientFailure(
                ex,
                activeRole == PlatformAuthRole.Bot ? YouTubeMonitorIssue.MissingBotAuth : YouTubeMonitorIssue.MissingBroadcasterAuth);
        }
        catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
        {
            if (Volatile.Read(ref _settingsRevision) != settingsRevision)
            {
                return TimeSpan.Zero;
            }

            var retryDelay = HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
            await PublishMonitoringStatusIfChangedAsync(
                StreamMonitoringStatus.Reconnecting,
                "повтор после ошибки API",
                activeRole).ConfigureAwait(false);
            return retryDelay;
        }
    }

    private async Task<TimeSpan> PollMultiChannelAsync(
        YouTubeSettings settings,
        IReadOnlyList<ChannelCatalogEntry> entries,
        bool forceActiveSample,
        long settingsRevision,
        CancellationToken ct)
    {
        var routing = channelCatalogStore!.GetPlatformSettings(StreamingPlatform.YouTube);
        var activeChannelId = ResolveActiveChannelId(routing, entries);

        var wantedIds = new HashSet<string>(entries.Select(static e => e.ChannelId), StringComparer.Ordinal);
        lock (_sync)
        {
            _targetChannelId = activeChannelId;
            foreach (var existingId in _slots.Keys.ToArray())
            {
                if (!wantedIds.Contains(existingId))
                {
                    _slots.Remove(existingId);
                    _lastPublicDiscoveryByChannel.Remove(existingId);
                }
            }

            foreach (var entry in entries)
            {
                var slot = GetOrCreateSlotLocked(entry);
                slot.UpdateMetadata(entry);
            }
        }

        var broadcasterGroups = new Dictionary<string, List<YouTubeChannelSlot>>(StringComparer.Ordinal);
        var botOnlySlots = new List<(YouTubeChannelSlot Slot, string BotContextId)>();
        var anyUnavailable = false;
        var anyActiveHealthy = false;
        var anyMissingBotAuthOnly = false;
        var anyMissingBroadcasterAuth = false;
        PlatformAuthRole? activeSlotRole = null;
        string? firstUnavailableDetail = null;
        string? activeUnavailableDetail = null;
        var activeInTransientBackoff = false;

        foreach (var entry in entries)
        {
            if (!IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }

            var slot = GetSlot(entry.ChannelId)!;
            var broadcasterAssignment = entry.GetAccountAssignment(PlatformAuthRole.Broadcaster);
            var broadcasterContextId = AuthContextIds.Normalize(broadcasterAssignment?.ContextId ?? entry.BroadcasterContextId);
            var broadcasterToken = await GetTokenAsync(PlatformAuthRole.Broadcaster, broadcasterContextId, ct).ConfigureAwait(false);
            var broadcasterAccount = accountsStore?.Load(PlatformAuthRole.Broadcaster, broadcasterContextId);
            var broadcasterMismatch = CheckContextOwnershipMismatch(slot, broadcasterAccount);

            var botAssignment = entry.GetAccountAssignment(PlatformAuthRole.Bot);
            var botContextId = AuthContextIds.Normalize(botAssignment?.ContextId ?? entry.BotContextId);
            var botToken = await GetTokenAsync(PlatformAuthRole.Bot, botContextId, ct).ConfigureAwait(false);
            if (!IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }

            var botAccount = accountsStore?.Load(PlatformAuthRole.Bot, botContextId);
            var botMismatch = CheckBotContextOwnershipMismatch(slot, botAccount);
            var hasUsableBot = publicDiscovery is not null && !string.IsNullOrWhiteSpace(botToken) && botMismatch is null;

            var broadcasterChannelBlank = string.IsNullOrWhiteSpace(broadcasterAccount?.ChannelId);
            var broadcasterVerifiedForChannel = broadcasterAssignment is { IsVerified: true }
                && string.Equals(broadcasterAssignment.Account.AccountId, slot.ChannelId, StringComparison.Ordinal);
            var broadcasterUnconfirmed = broadcasterChannelBlank && !broadcasterVerifiedForChannel;

            var currentActiveForEntry = ResolveActiveChannelId(routing, entries);
            var isEntryActive = string.Equals(slot.ChannelId, currentActiveForEntry, StringComparison.Ordinal);

            if (!string.IsNullOrWhiteSpace(broadcasterToken)
                && broadcasterMismatch is null
                && !(broadcasterUnconfirmed && hasUsableBot))
            {
                if (!broadcasterGroups.TryGetValue(broadcasterContextId, out var groupSlots))
                {
                    groupSlots = [];
                    broadcasterGroups[broadcasterContextId] = groupSlots;
                }

                groupSlots.Add(slot);
                if (isEntryActive)
                {
                    activeSlotRole = PlatformAuthRole.Broadcaster;
                }

                continue;
            }

            if (hasUsableBot)
            {
                botOnlySlots.Add((slot, botContextId));
                if (isEntryActive)
                {
                    activeSlotRole = PlatformAuthRole.Bot;
                }

                continue;
            }

            anyUnavailable = true;
            MonitoringUnavailableReason unavailableReason;
            string unavailableDetail;
            PlatformAuthRole unavailableRole;

            var isBotOnlyAssignment = botAssignment is not null && broadcasterAssignment is null;

            if (broadcasterMismatch is not null
                && (broadcasterAssignment is not null
                    || (!isBotOnlyAssignment && !string.IsNullOrWhiteSpace(broadcasterToken))))
            {
                unavailableReason = MonitoringUnavailableReason.AccessUnverified;
                unavailableDetail = broadcasterMismatch;
                unavailableRole = PlatformAuthRole.Broadcaster;
                anyMissingBroadcasterAuth = true;
            }
            else if (botAssignment is not null && botMismatch is not null)
            {
                unavailableReason = MonitoringUnavailableReason.AccessUnverified;
                unavailableDetail = botMismatch;
                unavailableRole = PlatformAuthRole.Bot;
                anyMissingBotAuthOnly = true;
            }
            else if (isBotOnlyAssignment)
            {
                unavailableReason = MonitoringUnavailableReason.MissingBotAuth;
                unavailableDetail = "нет авторизации бота для канала";
                unavailableRole = PlatformAuthRole.Bot;
                anyMissingBotAuthOnly = true;
            }
            else
            {
                unavailableReason = MonitoringUnavailableReason.MissingBroadcasterAuth;
                unavailableDetail = "нет авторизации ведущего для канала";
                unavailableRole = PlatformAuthRole.Broadcaster;
                anyMissingBroadcasterAuth = true;
            }

            if (isEntryActive)
            {
                activeSlotRole = unavailableRole;
                activeUnavailableDetail ??= unavailableDetail;
            }

            firstUnavailableDetail ??= unavailableDetail;
            await MarkSlotUnavailableAsync(
                slot,
                unavailableReason,
                unavailableDetail,
                isActive: isEntryActive).ConfigureAwait(false);
        }

        if (broadcasterGroups.Count == 0 && botOnlySlots.Count == 0)
        {
            var failureRole = activeSlotRole
                ?? (anyMissingBotAuthOnly && !anyMissingBroadcasterAuth
                    ? PlatformAuthRole.Bot
                    : PlatformAuthRole.Broadcaster);
            await PublishMonitoringStatusIfChangedAsync(
                StreamMonitoringStatus.Failed,
                activeUnavailableDetail ?? firstUnavailableDetail ?? "нет авторизации стримера",
                failureRole).ConfigureAwait(false);
            return DisabledCheckInterval;
        }

        var offlineInterval = broadcasterGroups.Count > 0
            ? ComputeEffectiveOfflineInterval(broadcasterGroups.Count, settings.DailyQuotaBudget)
            : OfflinePollInterval;
        var nextDelay = offlineInterval;
        var quotaHit = false;
        PlatformAuthRole? activeQuotaRole = null;
        var quotaDelay = QuotaWaitCheckCap;
        var anyBroadcasterUnauthorized = false;
        var anyBotUnauthorized = false;
        Exception? lastTransientError = null;

        foreach (var (contextId, contextSlots) in broadcasterGroups)
        {
            if (!IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }

            try
            {
                var now = timeProvider.GetUtcNow();
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                var activeSlot = contextSlots.FirstOrDefault(s => string.Equals(s.ChannelId, currentActiveId, StringComparison.Ordinal));
                var backgroundDue = false;
                foreach (var bgSlot in contextSlots)
                {
                    if (string.Equals(bgSlot.ChannelId, currentActiveId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (bgSlot.NextRetryAtUtc is { } bgRetryAt && now < bgRetryAt)
                    {
                        var remainingBgRetry = bgRetryAt - now;
                        if (remainingBgRetry > TimeSpan.Zero && remainingBgRetry < nextDelay)
                        {
                            nextDelay = remainingBgRetry;
                        }

                        continue;
                    }

                    var lastBgDiscovery = bgSlot.LastDiscoveryAtUtc
                        ?? (_lastDiscoveryByContext.TryGetValue(contextId, out var ctxDiscAt) ? ctxDiscAt : (DateTimeOffset?)null);
                    if (lastBgDiscovery is null || now - lastBgDiscovery.Value >= offlineInterval)
                    {
                        backgroundDue = true;
                    }
                    else
                    {
                        var remainingBgDisc = offlineInterval - (now - lastBgDiscovery.Value);
                        if (remainingBgDisc > TimeSpan.Zero && remainingBgDisc < nextDelay)
                        {
                            nextDelay = remainingBgDisc;
                        }
                    }
                }

                var activeNeedsDiscovery = false;
                if (activeSlot is not null)
                {
                    var activeSelectionChanged = !string.Equals(
                        activeSlot.ActiveSelectionVideoId,
                        NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(activeSlot.ChannelId)),
                        StringComparison.Ordinal);
                    var activeModeChanged = activeSlot.ActiveDiscoveryRole != PlatformAuthRole.Broadcaster
                        || !string.Equals(activeSlot.ActiveDiscoveryContextId, contextId, StringComparison.Ordinal);

                    if (activeSlot.ActiveBroadcast is null || activeSelectionChanged || activeModeChanged)
                    {
                        if (!forceActiveSample && activeSlot.NextRetryAtUtc is { } activeRetryAt && now < activeRetryAt)
                        {
                            var remainingActiveRetry = activeRetryAt - now;
                            if (remainingActiveRetry > TimeSpan.Zero && remainingActiveRetry < nextDelay)
                            {
                                nextDelay = remainingActiveRetry;
                            }

                            if (activeSlot.Availability == ChannelMonitoringAvailability.Unavailable
                                && activeSlot.Reason == MonitoringUnavailableReason.TransportDisconnected)
                            {
                                activeInTransientBackoff = true;
                            }
                        }
                        else if (forceActiveSample
                            || activeSelectionChanged
                            || activeModeChanged
                            || activeSlot.LastDiscoveryAtUtc is null
                            || now - activeSlot.LastDiscoveryAtUtc.Value >= offlineInterval)
                        {
                            activeNeedsDiscovery = true;
                        }
                        else
                        {
                            var remainingActiveDisc = offlineInterval - (now - activeSlot.LastDiscoveryAtUtc.Value);
                            if (remainingActiveDisc > TimeSpan.Zero && remainingActiveDisc < nextDelay)
                            {
                                nextDelay = remainingActiveDisc;
                            }
                        }
                    }
                }

                var needsDiscovery = backgroundDue || activeNeedsDiscovery;

                IReadOnlyList<YouTubeBroadcastSummary> broadcasts = [];
                if (needsDiscovery)
                {
                    broadcasts = await broadcastClient.ListActiveBroadcastsAsync(contextId, ct).ConfigureAwait(false);
                    if (!IsCurrentSettings(settingsRevision))
                    {
                        return TimeSpan.Zero;
                    }

                    var discoveryCompletedAt = timeProvider.GetUtcNow();
                    _lastDiscoveryByContext[contextId] = discoveryCompletedAt;
                    foreach (var discoveredSlot in contextSlots)
                    {
                        discoveredSlot.LastDiscoveryAtUtc = discoveryCompletedAt;
                    }
                }

                currentActiveId = ResolveActiveChannelId(routing, entries);
                lock (_sync)
                {
                    _targetChannelId = currentActiveId;
                }

                foreach (var slot in contextSlots)
                {
                    currentActiveId = ResolveActiveChannelId(routing, entries);
                    var isActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                    if (isActive)
                    {
                        activeSlotRole = PlatformAuthRole.Broadcaster;
                    }

                    if (!needsDiscovery)
                    {
                        if (isActive)
                        {
                            if (slot.ActiveBroadcast is { } currentActive)
                            {
                                var sampleNow = timeProvider.GetUtcNow();
                                if (!forceActiveSample && slot.NextRetryAtUtc is { } retryAt && sampleNow < retryAt)
                                {
                                    var remainingRetry = retryAt - sampleNow;
                                    if (remainingRetry > TimeSpan.Zero && remainingRetry < nextDelay)
                                    {
                                        nextDelay = remainingRetry;
                                    }

                                    if (slot.Availability == ChannelMonitoringAvailability.Unavailable
                                        && slot.Reason == MonitoringUnavailableReason.TransportDisconnected)
                                    {
                                        activeInTransientBackoff = true;
                                    }
                                }
                                else if (!forceActiveSample
                                    && slot.LastSampleAtUtc is { } lastSample
                                    && sampleNow - lastSample < OnlinePollInterval)
                                {
                                    var remainingSample = OnlinePollInterval - (sampleNow - lastSample);
                                    if (remainingSample > TimeSpan.Zero && remainingSample < nextDelay)
                                    {
                                        nextDelay = remainingSample;
                                    }

                                    anyActiveHealthy = true;
                                }
                                else
                                {
                                    var delay = await PollSlotActiveVideoSampleAsync(
                                        slot,
                                        currentActive,
                                        PlatformAuthRole.Broadcaster,
                                        contextId,
                                        routing,
                                        entries,
                                        settingsRevision,
                                        ct).ConfigureAwait(false);

                                    if (string.Equals(slot.ChannelId, ResolveActiveChannelId(routing, entries), StringComparison.Ordinal))
                                    {
                                        if (delay < nextDelay)
                                        {
                                            nextDelay = delay;
                                        }

                                        anyActiveHealthy = true;
                                    }
                                }
                            }
                            else if (slot.Availability == ChannelMonitoringAvailability.Active)
                            {
                                anyActiveHealthy = true;
                            }
                        }

                        continue;
                    }

                    if (!isActive && slot.NextRetryAtUtc is { } bgSlotRetryAt && now < bgSlotRetryAt)
                    {
                        continue;
                    }

                    var matches = broadcasts
                        .Where(b => string.Equals(b.ChannelId, slot.ChannelId, StringComparison.Ordinal))
                        .ToArray();

                    slot.ConsecutiveFailures = 0;
                    slot.NextRetryAtUtc = null;

                    await ProcessSlotDiscoveryAsync(
                        slot,
                        matches,
                        PlatformAuthRole.Broadcaster,
                        contextId,
                        isActive,
                        initialSample: null,
                        routing,
                        entries,
                        settingsRevision,
                        ct).ConfigureAwait(false);

                    if (string.Equals(slot.ChannelId, ResolveActiveChannelId(routing, entries), StringComparison.Ordinal))
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
            catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException && !IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }
            catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
            {
                var isServerExhausted = ex.Kind == YouTubeApiErrorKind.QuotaExceeded;
                if (isServerExhausted && !ex.QuotaExhaustionRecorded)
                {
                    quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Broadcaster);
                }

                quotaHit = true;
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                foreach (var slot in contextSlots)
                {
                    var isSlotActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                    if (isSlotActive)
                    {
                        activeQuotaRole = PlatformAuthRole.Broadcaster;
                        activeSlotRole = PlatformAuthRole.Broadcaster;
                    }

                    await MarkSlotUnavailableAsync(
                        slot,
                        MonitoringUnavailableReason.QuotaExhausted,
                        "квота исчерпана",
                        isActive: isSlotActive).ConfigureAwait(false);
                }

                var contextQuotaDelay = ComputeQuotaRetryDelay(isServerExhausted);
                if (contextQuotaDelay < quotaDelay)
                {
                    quotaDelay = contextQuotaDelay;
                }
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
            {
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                foreach (var slot in contextSlots)
                {
                    var isSlotActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                    var botAssignment = slot.Entry.GetAccountAssignment(PlatformAuthRole.Bot);
                    var botContextId = AuthContextIds.Normalize(botAssignment?.ContextId ?? slot.Entry.BotContextId);
                    var botToken = await GetTokenAsync(PlatformAuthRole.Bot, botContextId, ct).ConfigureAwait(false);
                    var botAccount = accountsStore?.Load(PlatformAuthRole.Bot, botContextId);
                    var botMismatch = CheckBotContextOwnershipMismatch(slot, botAccount);
                    var hasUsableBot = publicDiscovery is not null && !string.IsNullOrWhiteSpace(botToken) && botMismatch is null;

                    if (hasUsableBot)
                    {
                        slot.LastDiscoveryAtUtc = null;
                        botOnlySlots.Add((slot, botContextId));
                        if (isSlotActive)
                        {
                            activeSlotRole = PlatformAuthRole.Bot;
                        }

                        continue;
                    }

                    anyUnavailable = true;
                    anyBroadcasterUnauthorized = true;
                    firstUnavailableDetail ??= "нет авторизации ведущего";
                    if (isSlotActive)
                    {
                        activeUnavailableDetail ??= "нет авторизации ведущего";
                        activeSlotRole = PlatformAuthRole.Broadcaster;
                    }

                    await MarkSlotUnavailableAsync(
                        slot,
                        MonitoringUnavailableReason.MissingBroadcasterAuth,
                        "токен ведущего отклонён",
                        isActive: isSlotActive).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
            {
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                var affectsActive = contextSlots.Any(s => string.Equals(s.ChannelId, currentActiveId, StringComparison.Ordinal));
                var nowFailure = timeProvider.GetUtcNow();
                var maxGroupDelay = TransientBackoffSteps[0];

                foreach (var slot in contextSlots)
                {
                    slot.ConsecutiveFailures++;
                    var slotDelay = TransientBackoffSteps[Math.Clamp(slot.ConsecutiveFailures - 1, 0, TransientBackoffSteps.Length - 1)];
                    slot.NextRetryAtUtc = nowFailure + slotDelay;
                    if (slotDelay > maxGroupDelay)
                    {
                        maxGroupDelay = slotDelay;
                    }

                    await MarkSlotTransientUnavailableAsync(slot).ConfigureAwait(false);
                }

                if (affectsActive)
                {
                    lastTransientError = ex;
                    activeSlotRole = PlatformAuthRole.Broadcaster;
                    var retryDelay = HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
                    if (retryDelay < nextDelay)
                    {
                        nextDelay = retryDelay;
                    }
                }
                else
                {
                    logger.LogWarning(ex, "YouTubeStreamMonitor: сбой фонового опроса группы ведущего {ContextId}", contextId);
                    if (maxGroupDelay < nextDelay)
                    {
                        nextDelay = maxGroupDelay;
                    }
                }
            }
        }

        foreach (var (slot, botContextId) in botOnlySlots)
        {
            if (!IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }

            try
            {
                var now = timeProvider.GetUtcNow();
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                lock (_sync)
                {
                    _targetChannelId = currentActiveId;
                }

                var isActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                if (isActive)
                {
                    activeSlotRole = PlatformAuthRole.Bot;
                }

                var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(slot.ChannelId));
                var selectionChanged = !string.Equals(slot.ActiveSelectionVideoId, selectedVideoId, StringComparison.Ordinal);
                var roleOrContextChanged = slot.ActiveDiscoveryRole != PlatformAuthRole.Bot
                    || !string.Equals(slot.ActiveDiscoveryContextId, botContextId, StringComparison.Ordinal);

                if (isActive && slot.ActiveBroadcast is { } currentActive && !selectionChanged && !roleOrContextChanged)
                {
                    if (!forceActiveSample && slot.NextRetryAtUtc is { } sampleRetryAt && now < sampleRetryAt)
                    {
                        var remainingRetry = sampleRetryAt - now;
                        if (remainingRetry > TimeSpan.Zero && remainingRetry < nextDelay)
                        {
                            nextDelay = remainingRetry;
                        }

                        if (slot.Availability == ChannelMonitoringAvailability.Unavailable
                            && slot.Reason == MonitoringUnavailableReason.TransportDisconnected)
                        {
                            activeInTransientBackoff = true;
                        }

                        continue;
                    }

                    if (!forceActiveSample
                        && slot.LastSampleAtUtc is { } lastSample
                        && now - lastSample < OnlinePollInterval)
                    {
                        var remainingSample = OnlinePollInterval - (now - lastSample);
                        if (remainingSample > TimeSpan.Zero && remainingSample < nextDelay)
                        {
                            nextDelay = remainingSample;
                        }

                        anyActiveHealthy = true;
                        continue;
                    }

                    var delay = await PollSlotActiveVideoSampleAsync(
                        slot,
                        currentActive,
                        PlatformAuthRole.Bot,
                        botContextId,
                        routing,
                        entries,
                        settingsRevision,
                        ct).ConfigureAwait(false);

                    if (string.Equals(slot.ChannelId, ResolveActiveChannelId(routing, entries), StringComparison.Ordinal))
                    {
                        if (delay < nextDelay)
                        {
                            nextDelay = delay;
                        }

                        anyActiveHealthy = true;
                    }

                    continue;
                }

                if (!(isActive && forceActiveSample) && slot.NextRetryAtUtc is { } retryAt && now < retryAt)
                {
                    var remainingRetry = retryAt - now;
                    if (remainingRetry > TimeSpan.Zero && remainingRetry < nextDelay)
                    {
                        nextDelay = remainingRetry;
                    }

                    if (isActive
                        && slot.Availability == ChannelMonitoringAvailability.Unavailable
                        && slot.Reason == MonitoringUnavailableReason.TransportDisconnected)
                    {
                        activeInTransientBackoff = true;
                    }

                    continue;
                }

                var forceDiscovery = isActive && (forceActiveSample || selectionChanged || roleOrContextChanged || slot.ActiveBroadcast is not null);
                DateTimeOffset? lastDiscoveryAt = slot.LastDiscoveryAtUtc;
                if (lastDiscoveryAt is null)
                {
                    lock (_sync)
                    {
                        if (_lastPublicDiscoveryByChannel.TryGetValue(slot.ChannelId, out var cachedAt))
                        {
                            lastDiscoveryAt = cachedAt;
                        }
                    }
                }

                if (!forceDiscovery && lastDiscoveryAt is { } lastDisc && now - lastDisc < OfflinePollInterval)
                {
                    var remainingDisc = OfflinePollInterval - (now - lastDisc);
                    if (remainingDisc > TimeSpan.Zero && remainingDisc < nextDelay)
                    {
                        nextDelay = remainingDisc;
                    }

                    if (isActive && slot.Availability == ChannelMonitoringAvailability.Active)
                    {
                        anyActiveHealthy = true;
                    }

                    continue;
                }

                var discovered = await publicDiscovery!.FindActiveLiveVideoAsync(slot.ChannelId, ct).ConfigureAwait(false);
                if (!IsCurrentSettings(settingsRevision))
                {
                    return TimeSpan.Zero;
                }

                currentActiveId = ResolveActiveChannelId(routing, entries);
                lock (_sync)
                {
                    _targetChannelId = currentActiveId;
                }

                isActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);

                YouTubeBroadcastSummary[] matches = [];
                YouTubeVideoLiveSample? initialSample = null;

                if (discovered is not null)
                {
                    initialSample = await GetVideoLiveSampleForRoleAsync(
                        discovered.VideoId,
                        PlatformAuthRole.Bot,
                        botContextId,
                        ct).ConfigureAwait(false);
                    if (!IsCurrentSettings(settingsRevision))
                    {
                        return TimeSpan.Zero;
                    }

                    slot.LastSampleAtUtc = timeProvider.GetUtcNow();
                    currentActiveId = ResolveActiveChannelId(routing, entries);
                    lock (_sync)
                    {
                        _targetChannelId = currentActiveId;
                    }

                    isActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);

                    if (initialSample is not null
                        && initialSample.ActualStartTimeUtc.HasValue
                        && initialSample.ActualEndTimeUtc is null
                        && !string.IsNullOrWhiteSpace(initialSample.ChannelId)
                        && string.Equals(initialSample.ChannelId, slot.ChannelId, StringComparison.Ordinal))
                    {
                        var startedAt = initialSample.ActualStartTimeUtc.Value;
                        var channelTitle = !string.IsNullOrWhiteSpace(initialSample.ChannelTitle)
                            ? initialSample.ChannelTitle
                            : discovered.ChannelTitle;
                        var title = !string.IsNullOrWhiteSpace(initialSample.Title)
                            ? initialSample.Title
                            : discovered.Title;
                        var liveChatId = string.IsNullOrWhiteSpace(initialSample.ActiveLiveChatId)
                            ? string.Empty
                            : initialSample.ActiveLiveChatId.Trim();

                        matches =
                        [
                            new YouTubeBroadcastSummary(
                                discovered.VideoId,
                                slot.ChannelId,
                                channelTitle,
                                liveChatId,
                                title,
                                startedAt.ToUniversalTime())
                            {
                                IsChatDisabled = initialSample.HasLiveStreamingDetails && liveChatId.Length == 0,
                            },
                        ];
                    }
                }

                var discoveryCompletedAt = timeProvider.GetUtcNow();
                slot.LastDiscoveryAtUtc = discoveryCompletedAt;
                lock (_sync)
                {
                    _lastPublicDiscoveryByChannel[slot.ChannelId] = discoveryCompletedAt;
                }

                slot.ConsecutiveFailures = 0;
                slot.NextRetryAtUtc = null;

                await ProcessSlotDiscoveryAsync(
                    slot,
                    matches,
                    PlatformAuthRole.Bot,
                    botContextId,
                    isActive,
                    initialSample,
                    routing,
                    entries,
                    settingsRevision,
                    ct).ConfigureAwait(false);

                if (string.Equals(slot.ChannelId, ResolveActiveChannelId(routing, entries), StringComparison.Ordinal))
                {
                    anyActiveHealthy = true;
                    var slotDelay = slot.StateMachine.CurrentStatus == StreamStatus.Online
                        ? OnlinePollInterval
                        : OfflinePollInterval;
                    if (slotDelay < nextDelay)
                    {
                        nextDelay = slotDelay;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException && !IsCurrentSettings(settingsRevision))
            {
                return TimeSpan.Zero;
            }
            catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
            {
                var isServerExhausted = ex.Kind == YouTubeApiErrorKind.QuotaExceeded;
                if (isServerExhausted && !ex.QuotaExhaustionRecorded)
                {
                    quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Bot);
                }

                quotaHit = true;
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                var isSlotActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                if (isSlotActive)
                {
                    activeQuotaRole = PlatformAuthRole.Bot;
                    activeSlotRole = PlatformAuthRole.Bot;
                }

                await MarkSlotUnavailableAsync(
                    slot,
                    MonitoringUnavailableReason.QuotaExhausted,
                    "квота исчерпана",
                    isActive: isSlotActive).ConfigureAwait(false);

                var slotQuotaDelay = ComputeQuotaRetryDelay(isServerExhausted);
                if (slotQuotaDelay < quotaDelay)
                {
                    quotaDelay = slotQuotaDelay;
                }
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
            {
                anyUnavailable = true;
                anyBotUnauthorized = true;
                firstUnavailableDetail ??= "нет авторизации бота";
                var currentActiveId = ResolveActiveChannelId(routing, entries);
                var isSlotActive = string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal);
                if (isSlotActive)
                {
                    activeUnavailableDetail ??= "нет авторизации бота";
                    activeSlotRole = PlatformAuthRole.Bot;
                }

                await MarkSlotUnavailableAsync(
                    slot,
                    MonitoringUnavailableReason.MissingBotAuth,
                    "токен бота отклонён",
                    isActive: isSlotActive).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
            {
                slot.ConsecutiveFailures++;
                var slotDelay = TransientBackoffSteps[Math.Clamp(slot.ConsecutiveFailures - 1, 0, TransientBackoffSteps.Length - 1)];
                slot.NextRetryAtUtc = timeProvider.GetUtcNow() + slotDelay;

                await MarkSlotTransientUnavailableAsync(slot).ConfigureAwait(false);

                var currentActiveId = ResolveActiveChannelId(routing, entries);
                if (string.Equals(slot.ChannelId, currentActiveId, StringComparison.Ordinal))
                {
                    lastTransientError = ex;
                    activeSlotRole = PlatformAuthRole.Bot;
                    var retryDelay = HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
                    if (retryDelay < nextDelay)
                    {
                        nextDelay = retryDelay;
                    }
                }
                else
                {
                    logger.LogWarning(
                        ex,
                        "YouTubeStreamMonitor: сбой фонового опроса канала {ChannelId} (попытка #{Failures}), повтор через {DelaySeconds} с",
                        slot.ChannelId,
                        slot.ConsecutiveFailures,
                        (int)slotDelay.TotalSeconds);
                    if (slotDelay < nextDelay)
                    {
                        nextDelay = slotDelay;
                    }
                }
            }
        }

        if (!IsCurrentSettings(settingsRevision))
        {
            return TimeSpan.Zero;
        }

        var finalActiveId = ResolveActiveChannelId(routing, entries);
        var statusRole = activeSlotRole
            ?? (botOnlySlots.Any(x => string.Equals(x.Slot.ChannelId, finalActiveId, StringComparison.Ordinal))
                ? PlatformAuthRole.Bot
                : (broadcasterGroups.Count > 0 || (anyBroadcasterUnauthorized && !anyBotUnauthorized)
                    ? PlatformAuthRole.Broadcaster
                    : PlatformAuthRole.Bot));

        if (quotaHit)
        {
            if (activeQuotaRole is not null || !anyActiveHealthy)
            {
                await PublishMonitoringStatusIfChangedAsync(
                    StreamMonitoringStatus.Failed,
                    "квота исчерпана",
                    activeQuotaRole ?? statusRole).ConfigureAwait(false);
            }
            else
            {
                await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, statusRole).ConfigureAwait(false);
            }

            if (quotaDelay < nextDelay)
            {
                nextDelay = quotaDelay;
            }
        }
        else if ((lastTransientError is not null || activeInTransientBackoff) && !anyActiveHealthy)
        {
            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Reconnecting, "повтор после ошибки API", statusRole).ConfigureAwait(false);
        }
        else if (anyUnavailable)
        {
            await PublishMonitoringStatusIfChangedAsync(
                anyActiveHealthy ? StreamMonitoringStatus.Connected : StreamMonitoringStatus.Failed,
                anyActiveHealthy ? null : (activeUnavailableDetail ?? firstUnavailableDetail),
                statusRole).ConfigureAwait(false);
        }
        else
        {
            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, statusRole).ConfigureAwait(false);
        }

        return nextDelay;
    }

    private async Task ProcessSlotDiscoveryAsync(
        YouTubeChannelSlot slot,
        YouTubeBroadcastSummary[] matches,
        PlatformAuthRole role,
        string contextId,
        bool isActive,
        YouTubeVideoLiveSample? initialSample,
        PlatformChannelRoutingSettings routing,
        IReadOnlyList<ChannelCatalogEntry> entries,
        long settingsRevision,
        CancellationToken ct)
    {
        if (!IsCurrentSettings(settingsRevision))
        {
            return;
        }

        var previousSnapshot = slot.ToMonitoringSnapshot();
        var nowUtc = timeProvider.GetUtcNow();
        slot.LastPollAt = nowUtc;
        isActive = isActive && IsStillActiveChannel(slot.ChannelId, routing, entries);

        lock (_sync)
        {
            _lastPollAt = nowUtc;
            if (isActive)
            {
                _consecutiveFailures = 0;
            }
        }

        if (matches.Length == 0)
        {
            var transition = slot.StateMachine.ApplyOffline();
            var previousActive = slot.ActiveBroadcast;
            slot.ActiveBroadcast = null;
            slot.ActiveSelectionVideoId = null;
            slot.ActiveDiscoveryRole = null;
            slot.ActiveDiscoveryContextId = null;
            slot.LastSampleAtUtc = null;
            slot.ActiveCandidates = [];
            slot.Availability = ChannelMonitoringAvailability.Active;
            slot.Reason = MonitoringUnavailableReason.None;
            slot.Detail = null;
            var isBaseline = !slot.BaselinePublished;
            slot.BaselinePublished = true;

            if (isActive && IsStillActiveChannel(slot.ChannelId, routing, entries))
            {
                var activeTransition = _stateMachine.ApplyOffline();
                bool stillActiveLocked;
                lock (_sync)
                {
                    stillActiveLocked = IsStillActiveChannelLocked(slot.ChannelId, routing, entries);
                    if (stillActiveLocked)
                    {
                        _activeBroadcast = null;
                        _activeSelectionVideoId = null;
                        _activeDiscoveryRole = null;
                        _activeCandidates = [];
                        _issue = YouTubeMonitorIssue.None;
                        _transport = ResolveMonitoringTransport(role, isActiveBroadcast: false);
                        _lastLoggedMultiSetKey = null;
                    }
                }

                if (stillActiveLocked)
                {
                    if (previousActive is not null)
                    {
                        await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
                    }

                    await PublishOfflineIfNeededAsync(activeTransition, slot.ChannelId).ConfigureAwait(false);
                }
            }

            if (transition.Transitioned || isBaseline || previousSnapshot.Availability != ChannelMonitoringAvailability.Active)
            {
                await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isBaseline), isCatchUp: isBaseline).ConfigureAwait(false);
            }

            if (previousSnapshot.Availability != slot.Availability || previousSnapshot.Reason != slot.Reason)
            {
                await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
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
            slot.ActiveDiscoveryRole = null;
            slot.ActiveDiscoveryContextId = null;
            slot.LastSampleAtUtc = null;
            slot.ActiveCandidates = matches;
            slot.Availability = ChannelMonitoringAvailability.Degraded;
            slot.Reason = MonitoringUnavailableReason.MultipleActiveBroadcasts;
            slot.Detail = "несколько активных эфиров";

            if (isActive && IsStillActiveChannel(slot.ChannelId, routing, entries))
            {
                await HandleAmbiguousBroadcastsAsync(slot.ChannelId, matches, previousActive, role, publishStatus: false, routing, entries).ConfigureAwait(false);
            }

            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
            return;
        }

        slot.ActiveBroadcast = selected;
        slot.ActiveSelectionVideoId = selectedVideoId;
        slot.ActiveDiscoveryRole = role;
        slot.ActiveDiscoveryContextId = contextId;
        slot.ActiveCandidates = matches;
        slot.Availability = ChannelMonitoringAvailability.Active;
        slot.Reason = MonitoringUnavailableReason.None;
        slot.Detail = null;

        var initialTitle = !string.IsNullOrWhiteSpace(initialSample?.Title) ? initialSample.Title : selected.Title;
        var initialStartedAt = (initialSample?.ActualStartTimeUtc ?? selected.StartedAtUtc).UtcDateTime;
        var initialViewers = initialSample?.ConcurrentViewers ?? 0;
        var streamInfo = BuildStreamInfo(selected, slot.ChannelId, initialTitle, initialStartedAt, initialViewers);
        var slotTransition = initialSample?.ConcurrentViewers.HasValue == true
            ? slot.StateMachine.ApplyOnlineSnapshot(streamInfo)
            : slot.StateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        var isInitial = !slot.BaselinePublished;
        slot.BaselinePublished = true;

        if (isActive && IsStillActiveChannel(slot.ChannelId, routing, entries))
        {
            await ActivateBroadcastAsync(
                slot.ChannelId,
                selected,
                matches,
                _activeBroadcast,
                selectedVideoId,
                initialSample,
                publishStatus: false,
                statusRole: role,
                routing: routing,
                entries: entries,
                expectedSettingsRevision: settingsRevision).ConfigureAwait(false);
            if (initialSample is null
                && !string.IsNullOrWhiteSpace(selected.VideoId)
                && IsCurrentSettings(settingsRevision)
                && IsStillActiveChannel(slot.ChannelId, routing, entries))
            {
                _ = await PollSlotActiveVideoSampleAsync(slot, selected, role, contextId, routing, entries, settingsRevision, ct).ConfigureAwait(false);
            }
        }

        if (!IsCurrentSettings(settingsRevision))
        {
            return;
        }

        if (slotTransition.Transitioned || slotTransition.SnapshotChanged || isInitial || previousSnapshot.Availability != ChannelMonitoringAvailability.Active)
        {
            await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isInitial), isCatchUp: isInitial).ConfigureAwait(false);
        }

        if (previousSnapshot.Availability != slot.Availability || previousSnapshot.Reason != slot.Reason)
        {
            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan> PollSlotActiveVideoSampleAsync(
        YouTubeChannelSlot slot,
        YouTubeBroadcastSummary active,
        PlatformAuthRole role,
        string contextId,
        PlatformChannelRoutingSettings routing,
        IReadOnlyList<ChannelCatalogEntry> entries,
        long settingsRevision,
        CancellationToken ct)
    {
        var previousSnapshot = slot.ToMonitoringSnapshot();
        var sample = await GetVideoLiveSampleForRoleAsync(active.VideoId, role, contextId, ct).ConfigureAwait(false);
        if (!IsCurrentSettings(settingsRevision))
        {
            return TimeSpan.Zero;
        }

        var nowUtc = timeProvider.GetUtcNow();
        slot.LastPollAt = nowUtc;
        slot.LastSampleAtUtc = nowUtc;
        slot.ConsecutiveFailures = 0;
        slot.NextRetryAtUtc = null;

        var isActive = IsStillActiveChannel(slot.ChannelId, routing, entries);

        lock (_sync)
        {
            _lastPollAt = nowUtc;
            if (isActive)
            {
                _consecutiveFailures = 0;
            }
        }

        if (sample is null || sample.ActualEndTimeUtc is not null)
        {
            var slotTransition = slot.StateMachine.ApplyOffline();
            slot.ActiveBroadcast = null;
            slot.ActiveSelectionVideoId = null;
            slot.ActiveDiscoveryRole = null;
            slot.ActiveDiscoveryContextId = null;
            slot.LastSampleAtUtc = null;
            slot.ActiveCandidates = [];
            slot.Availability = ChannelMonitoringAvailability.Active;
            slot.Reason = MonitoringUnavailableReason.None;
            slot.Detail = null;
            slot.BaselinePublished = true;

            if (isActive)
            {
                bool stillActiveLocked;
                lock (_sync)
                {
                    stillActiveLocked = IsStillActiveChannelLocked(slot.ChannelId, routing, entries);
                    if (stillActiveLocked)
                    {
                        _activeBroadcast = null;
                        _activeSelectionVideoId = null;
                        _activeDiscoveryRole = null;
                        _activeCandidates = [];
                        _issue = YouTubeMonitorIssue.None;
                        _transport = ResolveMonitoringTransport(role, isActiveBroadcast: false);
                        _lastLoggedMultiSetKey = null;
                    }
                }

                if (stillActiveLocked)
                {
                    var activeTransition = _stateMachine.ApplyOffline();
                    await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
                    if (IsCurrentSettings(settingsRevision))
                    {
                        await PublishOfflineIfNeededAsync(activeTransition, slot.ChannelId).ConfigureAwait(false);
                    }
                }
            }

            if (IsCurrentSettings(settingsRevision) && slotTransition.Transitioned)
            {
                await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(), isCatchUp: false).ConfigureAwait(false);
            }

            if (IsCurrentSettings(settingsRevision)
                && (previousSnapshot.Availability != slot.Availability || previousSnapshot.Reason != slot.Reason))
            {
                await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
            }

            return OfflinePollInterval;
        }

        var effectiveBroadcast = ApplyChatSample(active, sample);
        var chatChanged = !ReferenceEquals(effectiveBroadcast, active);
        if (chatChanged)
        {
            slot.ActiveBroadcast = effectiveBroadcast;
        }

        if (chatChanged && isActive)
        {
            bool stillActiveLocked;
            lock (_sync)
            {
                stillActiveLocked = IsStillActiveChannelLocked(slot.ChannelId, routing, entries);
                if (stillActiveLocked)
                {
                    _activeBroadcast = effectiveBroadcast;
                }
            }

            if (stillActiveLocked)
            {
                if (!active.IsChatDisabled && effectiveBroadcast.IsChatDisabled)
                {
                    CoreLog.YouTubeChatDisabledOnBroadcast(logger, effectiveBroadcast.VideoId, slot.ChannelId);
                }

                await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(effectiveBroadcast), CancellationToken.None).ConfigureAwait(false);
                if (IsCurrentSettings(settingsRevision)
                    && (sample.HasLiveStreamingDetails || string.IsNullOrWhiteSpace(active.LiveChatId)))
                {
                    await eventBus.PublishAsync(
                        new PlatformChatAvailabilityChanged(StreamingPlatform.YouTube, !effectiveBroadcast.IsChatDisabled),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        if (!IsCurrentSettings(settingsRevision))
        {
            return TimeSpan.Zero;
        }

        var startedAtUtc = (sample.ActualStartTimeUtc ?? effectiveBroadcast.StartedAtUtc).UtcDateTime;
        var title = string.IsNullOrWhiteSpace(sample.Title) ? effectiveBroadcast.Title : sample.Title;
        var streamInfo = BuildStreamInfo(effectiveBroadcast, slot.ChannelId, title, startedAtUtc, sample.ConcurrentViewers ?? 0);

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
            bool stillActiveLocked;
            lock (_sync)
            {
                stillActiveLocked = IsStillActiveChannelLocked(slot.ChannelId, routing, entries);
                if (stillActiveLocked)
                {
                    _issue = YouTubeMonitorIssue.None;
                    _transport = ResolveMonitoringTransport(role, isActiveBroadcast: true);
                }
            }

            if (stillActiveLocked)
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
                            effectiveBroadcast.VideoId,
                            slot.ChannelId),
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    activeTransition = _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
                }

                await PublishOnlineOrMetadataAsync(activeTransition, slot.ChannelId, streamInfo, routing, entries, settingsRevision).ConfigureAwait(false);
            }
        }

        if (IsCurrentSettings(settingsRevision) && (slotOnlineTransition.Transitioned || slotOnlineTransition.SnapshotChanged || isBaseline))
        {
            await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(isBaseline), isCatchUp: isBaseline).ConfigureAwait(false);
        }

        if (IsCurrentSettings(settingsRevision)
            && (previousSnapshot.Availability != slot.Availability || previousSnapshot.Reason != slot.Reason))
        {
            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
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
        slot.ActiveDiscoveryRole = null;
        slot.ActiveDiscoveryContextId = null;
        slot.LastSampleAtUtc = null;
        slot.LastDiscoveryAtUtc = null;
        slot.NextRetryAtUtc = null;
        slot.ConsecutiveFailures = 0;
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
                _issue = reason switch
                {
                    MonitoringUnavailableReason.QuotaExhausted => YouTubeMonitorIssue.QuotaExhausted,
                    MonitoringUnavailableReason.MissingBotAuth => YouTubeMonitorIssue.MissingBotAuth,
                    MonitoringUnavailableReason.AccessUnverified => YouTubeMonitorIssue.AccessUnverified,
                    _ => YouTubeMonitorIssue.MissingBroadcasterAuth,
                };
                previousActive = _activeBroadcast;
                _activeBroadcast = null;
                _activeSelectionVideoId = null;
                _activeDiscoveryRole = null;
                _activeCandidates = [];
                _transport = YouTubeMonitoringTransport.None;
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

    private async Task MarkSlotTransientUnavailableAsync(YouTubeChannelSlot slot)
    {
        var changed = slot.Availability != ChannelMonitoringAvailability.Unavailable
            || slot.Reason != MonitoringUnavailableReason.TransportDisconnected
            || !string.Equals(slot.Detail, "ошибка опроса", StringComparison.Ordinal);

        slot.Availability = ChannelMonitoringAvailability.Unavailable;
        slot.Reason = MonitoringUnavailableReason.TransportDisconnected;
        slot.Detail = "ошибка опроса";

        if (changed)
        {
            await PublishSlotStateChangedAsync(slot).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeQuotaRetryDelay(bool isServerExhausted)
    {
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

    private static string? CheckBotContextOwnershipMismatch(YouTubeChannelSlot slot, Auth.YouTubeAccountSettings? account)
    {
        var assignment = slot.Entry.GetAccountAssignment(PlatformAuthRole.Bot);
        if (assignment is { IsVerified: false })
        {
            return "доступ аккаунта бота к каналу YouTube не подтверждён";
        }

        if (assignment is not null
            && account is not null
            && !string.Equals(assignment.Account.AccountId, account.ChannelId, StringComparison.Ordinal))
        {
            return "авторизация бота изменилась – требуется повторная проверка доступа";
        }

        return null;
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

    private async Task<TimeSpan> PollBroadcastDiscoveryAsync(
        string targetChannelId,
        long expectedSettingsRevision,
        CancellationToken ct)
    {
        var allBroadcasts = await broadcastClient.ListActiveBroadcastsAsync(ct).ConfigureAwait(false);
        var matches = allBroadcasts
            .Where(b => string.Equals(b.ChannelId, targetChannelId, StringComparison.Ordinal))
            .ToArray();

        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            if (_disposed || Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
            {
                return TimeSpan.Zero;
            }

            _lastPollAt = timeProvider.GetUtcNow();
            _consecutiveFailures = 0;
            previousActive = _activeBroadcast;
        }

        if (matches.Length == 0)
        {
            await HandleNoActiveBroadcastsAsync(
                targetChannelId,
                previousActive,
                PlatformAuthRole.Broadcaster,
                expectedSettingsRevision).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        var selectedVideoId = NormalizeSelectedVideoId(selectionStore.GetSelectedVideoId(targetChannelId));
        var selected = SelectBroadcast(selectedVideoId, matches);

        if (selected is null)
        {
            await HandleAmbiguousBroadcastsAsync(
                targetChannelId,
                matches,
                previousActive,
                expectedSettingsRevision: expectedSettingsRevision).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        await ActivateBroadcastAsync(
            targetChannelId,
            selected,
            matches,
            previousActive,
            selectedVideoId,
            expectedSettingsRevision: expectedSettingsRevision).ConfigureAwait(false);
        return OnlinePollInterval;
    }

    private async Task<TimeSpan> PollBotOnlyDiscoveryAsync(
        string targetChannelId,
        string? selectedVideoId,
        long expectedSettingsRevision,
        CancellationToken ct)
    {
        var discovered = await publicDiscovery!.FindActiveLiveVideoAsync(targetChannelId, ct).ConfigureAwait(false);

        YouTubeBroadcastSummary? previousActive;

        if (discovered is null)
        {
            lock (_sync)
            {
                if (_disposed || Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
                {
                    return TimeSpan.Zero;
                }

                _lastPollAt = timeProvider.GetUtcNow();
                _consecutiveFailures = 0;
                previousActive = _activeBroadcast;
            }

            await HandleNoActiveBroadcastsAsync(
                targetChannelId,
                previousActive,
                PlatformAuthRole.Bot,
                expectedSettingsRevision).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        if (Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
        {
            return TimeSpan.Zero;
        }

        var initialSample = await GetVideoLiveSampleForRoleAsync(
            discovered.VideoId,
            PlatformAuthRole.Bot,
            null,
            ct).ConfigureAwait(false);

        lock (_sync)
        {
            if (_disposed || Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
            {
                return TimeSpan.Zero;
            }

            _lastPollAt = timeProvider.GetUtcNow();
            _consecutiveFailures = 0;
            previousActive = _activeBroadcast;
        }

        if (initialSample is null
            || !initialSample.ActualStartTimeUtc.HasValue
            || initialSample.ActualEndTimeUtc is not null
            || string.IsNullOrWhiteSpace(initialSample.ChannelId)
            || !string.Equals(initialSample.ChannelId, targetChannelId, StringComparison.Ordinal))
        {
            await HandleNoActiveBroadcastsAsync(
                targetChannelId,
                previousActive,
                PlatformAuthRole.Bot,
                expectedSettingsRevision).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        var startedAt = initialSample.ActualStartTimeUtc.Value;
        var channelTitle = !string.IsNullOrWhiteSpace(initialSample.ChannelTitle)
            ? initialSample.ChannelTitle
            : discovered.ChannelTitle;
        var title = !string.IsNullOrWhiteSpace(initialSample.Title)
            ? initialSample.Title
            : discovered.Title;
        var liveChatId = string.IsNullOrWhiteSpace(initialSample.ActiveLiveChatId)
            ? string.Empty
            : initialSample.ActiveLiveChatId.Trim();

        var selected = new YouTubeBroadcastSummary(
            discovered.VideoId,
            targetChannelId,
            channelTitle,
            liveChatId,
            title,
            startedAt.ToUniversalTime())
        {
            IsChatDisabled = initialSample.HasLiveStreamingDetails && liveChatId.Length == 0,
        };

        await ActivateBroadcastAsync(
            targetChannelId,
            selected,
            [selected],
            previousActive,
            selectedVideoId,
            initialSample,
            publishStatus: true,
            statusRole: PlatformAuthRole.Bot,
            expectedSettingsRevision: expectedSettingsRevision).ConfigureAwait(false);
        return OnlinePollInterval;
    }

    private async Task<TimeSpan> PollActiveVideoSampleAsync(
        string targetChannelId,
        YouTubeBroadcastSummary active,
        PlatformAuthRole role,
        long expectedSettingsRevision,
        CancellationToken ct)
    {
        var sample = await GetVideoLiveSampleForRoleAsync(active.VideoId, role, null, ct).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow();

        lock (_sync)
        {
            if (_disposed || Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
            {
                return TimeSpan.Zero;
            }

            _lastPollAt = nowUtc;
            _consecutiveFailures = 0;
        }

        if (sample is null || sample.ActualEndTimeUtc is not null)
        {
            StatusTransition transition;
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, expectedSettingsRevision: expectedSettingsRevision))
                {
                    return TimeSpan.Zero;
                }

                transition = _stateMachine.ApplyOffline();
                _activeBroadcast = null;
                _activeSelectionVideoId = null;
                _activeDiscoveryRole = null;
                _activeCandidates = [];
                _issue = YouTubeMonitorIssue.None;
                _transport = ResolveMonitoringTransport(role, isActiveBroadcast: false);
                _lastLoggedMultiSetKey = null;
            }

            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);

            if (Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
            {
                return TimeSpan.Zero;
            }

            await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);
            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, role).ConfigureAwait(false);

            return OfflinePollInterval;
        }

        var effectiveBroadcast = ApplyChatSample(active, sample);
        if (!ReferenceEquals(effectiveBroadcast, active))
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, expectedSettingsRevision: expectedSettingsRevision))
                {
                    return TimeSpan.Zero;
                }

                _activeBroadcast = effectiveBroadcast;
            }

            if (!active.IsChatDisabled && effectiveBroadcast.IsChatDisabled)
            {
                CoreLog.YouTubeChatDisabledOnBroadcast(logger, effectiveBroadcast.VideoId, targetChannelId);
            }

            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(effectiveBroadcast), CancellationToken.None).ConfigureAwait(false);
            if (!IsCurrentSettings(expectedSettingsRevision))
            {
                return TimeSpan.Zero;
            }

            if (sample.HasLiveStreamingDetails || string.IsNullOrWhiteSpace(active.LiveChatId))
            {
                await eventBus.PublishAsync(
                    new PlatformChatAvailabilityChanged(StreamingPlatform.YouTube, !effectiveBroadcast.IsChatDisabled),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        var startedAtUtc = (sample.ActualStartTimeUtc ?? effectiveBroadcast.StartedAtUtc).UtcDateTime;
        var title = string.IsNullOrWhiteSpace(sample.Title) ? effectiveBroadcast.Title : sample.Title;
        var streamInfo = BuildStreamInfo(effectiveBroadcast, targetChannelId, title, startedAtUtc, sample.ConcurrentViewers ?? 0);

        StatusTransition onlineTransition;
        lock (_sync)
        {
            if (!CanMutateActiveChannelLocked(targetChannelId, expectedSettingsRevision: expectedSettingsRevision))
            {
                return TimeSpan.Zero;
            }

            _issue = YouTubeMonitorIssue.None;
            _transport = ResolveMonitoringTransport(role, isActiveBroadcast: true);
            onlineTransition = sample.ConcurrentViewers.HasValue
                ? _stateMachine.ApplyOnlineSnapshot(streamInfo)
                : _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        }

        if (sample.ConcurrentViewers.HasValue)
        {
            await eventBus.PublishAsync(
                new ViewerSampleObserved(
                    StreamingPlatform.YouTube,
                    targetChannelId,
                    sample.ConcurrentViewers.Value,
                    nowUtc,
                    effectiveBroadcast.VideoId,
                    targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }

        await PublishOnlineOrMetadataAsync(
            onlineTransition,
            targetChannelId,
            streamInfo,
            expectedSettingsRevision: expectedSettingsRevision).ConfigureAwait(false);

        if (Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
        {
            return TimeSpan.Zero;
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, role).ConfigureAwait(false);

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

    private static YouTubeBroadcastSummary ApplyChatSample(YouTubeBroadcastSummary broadcast, YouTubeVideoLiveSample sample)
    {
        var liveChatId = sample.ActiveLiveChatId?.Trim() ?? string.Empty;
        var isChatDisabled = liveChatId.Length == 0;
        if (isChatDisabled && !sample.HasLiveStreamingDetails)
        {
            return broadcast;
        }

        return broadcast.IsChatDisabled == isChatDisabled && string.Equals(broadcast.LiveChatId, liveChatId, StringComparison.Ordinal)
            ? broadcast
            : broadcast with { LiveChatId = liveChatId, IsChatDisabled = isChatDisabled };
    }

    private async Task ActivateBroadcastAsync(
        string targetChannelId,
        YouTubeBroadcastSummary selected,
        YouTubeBroadcastSummary[] matches,
        YouTubeBroadcastSummary? previousActive,
        string? selectedVideoId,
        YouTubeVideoLiveSample? initialSample = null,
        bool publishStatus = true,
        PlatformAuthRole statusRole = PlatformAuthRole.Broadcaster,
        PlatformChannelRoutingSettings? routing = null,
        IReadOnlyList<ChannelCatalogEntry>? entries = null,
        long? expectedSettingsRevision = null)
    {
        var switchedBroadcast = previousActive is not null
            && (!string.Equals(previousActive.VideoId, selected.VideoId, StringComparison.Ordinal)
                || !string.Equals(previousActive.ChannelId, targetChannelId, StringComparison.Ordinal)
                || !string.Equals(previousActive.LiveChatId, selected.LiveChatId, StringComparison.Ordinal)
                || previousActive.IsChatDisabled != selected.IsChatDisabled);

        if (!switchedBroadcast && previousActive is not null && _stateMachine.CurrentStatus == StreamStatus.Online)
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }

                _activeBroadcast = selected;
                _activeSelectionVideoId = selectedVideoId;
                _activeDiscoveryRole = statusRole;
                _activeCandidates = matches;
                _issue = YouTubeMonitorIssue.None;
                _transport = ResolveMonitoringTransport(statusRole, isActiveBroadcast: true);
                _lastLoggedMultiSetKey = null;
            }

            if (publishStatus)
            {
                await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, statusRole).ConfigureAwait(false);
            }

            return;
        }

        var hadActiveBroadcast = previousActive is not null;

        lock (_sync)
        {
            if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
            {
                return;
            }

            _activeBroadcast = null;
            _activeSelectionVideoId = selectedVideoId;
            _activeDiscoveryRole = statusRole;
            _activeCandidates = matches;
            _issue = YouTubeMonitorIssue.None;
            _lastLoggedMultiSetKey = null;
        }

        if (hadActiveBroadcast)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        var effectiveTitle = !string.IsNullOrWhiteSpace(initialSample?.Title) ? initialSample.Title : selected.Title;
        var effectiveStartedAt = (initialSample?.ActualStartTimeUtc ?? selected.StartedAtUtc).UtcDateTime;
        var effectiveViewers = initialSample?.ConcurrentViewers ?? 0;
        var streamInfo = BuildStreamInfo(selected, targetChannelId, effectiveTitle, effectiveStartedAt, effectiveViewers);
        StatusTransition transition;

        lock (_sync)
        {
            if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
            {
                return;
            }

            if (switchedBroadcast)
            {
                _stateMachine.ResetToUnknown();
            }

            transition = initialSample?.ConcurrentViewers.HasValue == true
                ? _stateMachine.ApplyOnlineSnapshot(streamInfo)
                : _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);
        }

        await PublishOnlineOrMetadataAsync(
            transition,
            targetChannelId,
            streamInfo,
            routing,
            entries,
            expectedSettingsRevision).ConfigureAwait(false);

        lock (_sync)
        {
            if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
            {
                return;
            }

            _activeBroadcast = selected;
            _activeDiscoveryRole = statusRole;
            _transport = ResolveMonitoringTransport(statusRole, isActiveBroadcast: true);
        }

        await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(selected), CancellationToken.None).ConfigureAwait(false);

        if (selected.IsChatDisabled
            && (previousActive is null
                || !previousActive.IsChatDisabled
                || !string.Equals(previousActive.VideoId, selected.VideoId, StringComparison.Ordinal)))
        {
            CoreLog.YouTubeChatDisabledOnBroadcast(logger, selected.VideoId, targetChannelId);
        }

        if (initialSample?.ConcurrentViewers is int viewers)
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }
            }

            await eventBus.PublishAsync(
                new ViewerSampleObserved(
                    StreamingPlatform.YouTube,
                    targetChannelId,
                    viewers,
                    timeProvider.GetUtcNow(),
                    selected.VideoId,
                    targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }

        if (selected.IsChatDisabled || !string.IsNullOrWhiteSpace(selected.LiveChatId))
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }
            }

            await eventBus.PublishAsync(
                new PlatformChatAvailabilityChanged(StreamingPlatform.YouTube, !selected.IsChatDisabled),
                CancellationToken.None).ConfigureAwait(false);
        }

        if (publishStatus)
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }
            }

            await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, statusRole).ConfigureAwait(false);
        }
    }

    private async Task HandleNoActiveBroadcastsAsync(
        string targetChannelId,
        YouTubeBroadcastSummary? previousActive,
        PlatformAuthRole statusRole = PlatformAuthRole.Broadcaster,
        long? expectedSettingsRevision = null)
    {
        StatusTransition transition;
        lock (_sync)
        {
            if (_disposed || (expectedSettingsRevision is { } rev && Volatile.Read(ref _settingsRevision) != rev))
            {
                return;
            }

            transition = _stateMachine.ApplyOffline();
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = [];
            _issue = YouTubeMonitorIssue.None;
            _transport = ResolveMonitoringTransport(statusRole, isActiveBroadcast: false);
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        if (expectedSettingsRevision is { } revAfter && Volatile.Read(ref _settingsRevision) != revAfter)
        {
            return;
        }

        await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);
        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Connected, null, statusRole).ConfigureAwait(false);
    }

    private async Task HandleAmbiguousBroadcastsAsync(
        string targetChannelId,
        YouTubeBroadcastSummary[] matches,
        YouTubeBroadcastSummary? previousActive,
        PlatformAuthRole statusRole = PlatformAuthRole.Broadcaster,
        bool publishStatus = true,
        PlatformChannelRoutingSettings? routing = null,
        IReadOnlyList<ChannelCatalogEntry>? entries = null,
        long? expectedSettingsRevision = null)
    {
        var setKey = string.Join(",", matches.Select(static m => m.VideoId).OrderBy(static id => id, StringComparer.Ordinal));
        var shouldLogWarning = false;

        lock (_sync)
        {
            if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
            {
                return;
            }

            _stateMachine.ResetToUnknown();
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = matches;
            _issue = YouTubeMonitorIssue.MultipleActiveBroadcasts;
            _transport = ResolveMonitoringTransport(statusRole, isActiveBroadcast: true);

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
        await eventBus.PublishAsync(
            new PlatformMultipleBroadcastsChanged(StreamingPlatform.YouTube, targetChannelId, matches.Length),
            CancellationToken.None).ConfigureAwait(false);

        if (publishStatus)
        {
            await PublishMonitoringStatusIfChangedAsync(
                StreamMonitoringStatus.Reconnecting,
                "несколько активных эфиров",
                statusRole).ConfigureAwait(false);
        }
    }

    private async Task TransitionToDisabledAsync()
    {
        var previousStatus = _stateMachine.CurrentStatus;
        _stateMachine.ResetToUnknown();
        YouTubeBroadcastSummary? previousActive;
        bool wasPublishedOnline;
        string offlineChannelId;
        PlatformAuthRole disconnectRole;

        lock (_sync)
        {
            _enabled = false;
            _issue = YouTubeMonitorIssue.None;
            _transport = YouTubeMonitoringTransport.None;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
            _consecutiveFailures = 0;
            disconnectRole = _lastMonitoringRole ?? PlatformAuthRole.Broadcaster;
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
                slot.ActiveSelectionVideoId = null;
                slot.ActiveDiscoveryRole = null;
                slot.ActiveDiscoveryContextId = null;
                slot.LastSampleAtUtc = null;
                slot.NextRetryAtUtc = null;
                slot.ConsecutiveFailures = 0;
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

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Disconnected, null, disconnectRole).ConfigureAwait(false);
    }

    private async Task TransitionToMissingAuthAsync(
        PlatformAuthRole role = PlatformAuthRole.Broadcaster,
        long? expectedSettingsRevision = null)
    {
        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            if (_disposed || (expectedSettingsRevision is { } rev && Volatile.Read(ref _settingsRevision) != rev))
            {
                return;
            }

            _stateMachine.ResetToUnknown();
            _issue = role == PlatformAuthRole.Bot
                ? YouTubeMonitorIssue.MissingBotAuth
                : YouTubeMonitorIssue.MissingBroadcasterAuth;
            _transport = YouTubeMonitoringTransport.None;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        var detail = role == PlatformAuthRole.Bot ? "нет авторизации бота" : "нет авторизации стримера";
        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, detail, role).ConfigureAwait(false);
    }

    private async Task TransitionToInvalidChannelAsync(
        PlatformAuthRole role = PlatformAuthRole.Broadcaster,
        long? expectedSettingsRevision = null)
    {
        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            if (_disposed || (expectedSettingsRevision is { } rev && Volatile.Read(ref _settingsRevision) != rev))
            {
                return;
            }

            _stateMachine.ResetToUnknown();
            _targetChannelId = null;
            _issue = YouTubeMonitorIssue.InvalidTargetChannel;
            _transport = YouTubeMonitoringTransport.None;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "неверный канал", role).ConfigureAwait(false);
    }

    private async Task<TimeSpan> HandleQuotaExceededAsync(
        bool isServerExhausted,
        bool exhaustionAlreadyRecorded,
        PlatformAuthRole role,
        long expectedSettingsRevision,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (isServerExhausted && !exhaustionAlreadyRecorded)
        {
            quotaGovernor.MarkExhaustedFromServer(role);
        }

        if (Volatile.Read(ref _settingsRevision) != expectedSettingsRevision)
        {
            return TimeSpan.Zero;
        }

        _stateMachine.ResetToUnknown();

        YouTubeBroadcastSummary? previousActive;

        lock (_sync)
        {
            _issue = YouTubeMonitorIssue.QuotaExhausted;
            _transport = YouTubeMonitoringTransport.None;
            previousActive = _activeBroadcast;
            _activeBroadcast = null;
            _activeSelectionVideoId = null;
            _activeDiscoveryRole = null;
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishMonitoringStatusIfChangedAsync(StreamMonitoringStatus.Failed, "квота исчерпана", role).ConfigureAwait(false);

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

    private async Task PublishOnlineOrMetadataAsync(
        StatusTransition transition,
        string targetChannelId,
        StreamInfo? streamSnapshot,
        PlatformChannelRoutingSettings? routing = null,
        IReadOnlyList<ChannelCatalogEntry>? entries = null,
        long? expectedSettingsRevision = null)
    {
        if (transition.Transitioned)
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }

                _publishedOnline = true;
                _lastPublishedChannelId = targetChannelId;
                _initialStatusResolved = true;
            }

            await eventBus.PublishAsync(
                new StreamWentOnline(StreamingPlatform.YouTube, targetChannelId, streamSnapshot, IsCatchUp: false, ChannelId: targetChannelId),
                CancellationToken.None).ConfigureAwait(false);
        }
        else if (transition.SnapshotChanged && streamSnapshot is { } updatedStream)
        {
            lock (_sync)
            {
                if (!CanMutateActiveChannelLocked(targetChannelId, routing, entries, expectedSettingsRevision))
                {
                    return;
                }
            }

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
        if (@event.Platform == StreamingPlatform.YouTube)
        {
            lock (_sync)
            {
                _lastDiscoveryByContext.Clear();
                _lastPublicDiscoveryByChannel.Clear();
                foreach (var slot in _slots.Values)
                {
                    if (slot.ActiveBroadcast is null || slot.Availability != ChannelMonitoringAvailability.Active)
                    {
                        slot.LastDiscoveryAtUtc = null;
                        slot.NextRetryAtUtc = null;
                        slot.ConsecutiveFailures = 0;
                    }
                }
            }

            Wake();
        }
    }

    private void OnSettingsChanged()
    {
        Interlocked.Increment(ref _settingsRevision);
        bool shouldWake;
        lock (_sync)
        {
            shouldWake = _wakeSubscriptionsAttached;
        }

        if (shouldWake)
        {
            Wake();
        }
    }

    private bool IsCurrentSettings(long revision) => !_disposed && Volatile.Read(ref _settingsRevision) == revision;

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

    private async Task PublishMonitoringStatusIfChangedAsync(
        StreamMonitoringStatus status,
        string? detail,
        PlatformAuthRole role = PlatformAuthRole.Broadcaster)
    {
        PlatformAuthRole? previousActiveRole = null;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_lastMonitoringRole == role
                && _lastMonitoringStatus == status
                && string.Equals(_lastMonitoringDetail, detail, StringComparison.Ordinal))
            {
                return;
            }

            if (_lastMonitoringRole is { } previousRole
                && previousRole != role
                && _lastMonitoringStatus is not null
                && _lastMonitoringStatus != StreamMonitoringStatus.Disconnected
                && status != StreamMonitoringStatus.Disconnected)
            {
                previousActiveRole = previousRole;
            }

            _lastMonitoringRole = role;
            _lastMonitoringStatus = status;
            _lastMonitoringDetail = detail;
        }

        if (previousActiveRole is { } prev)
        {
            logger.LogDebug("YouTube: публикация StreamMonitoringStatusChanged {Role} {Status} (–)", prev, StreamMonitoringStatus.Disconnected);
            await eventBus.PublishAsync(
                new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, prev, StreamMonitoringStatus.Disconnected, null),
                CancellationToken.None).ConfigureAwait(false);
        }

        logger.LogDebug("YouTube: публикация StreamMonitoringStatusChanged {Role} {Status} ({Detail})", role, status, detail ?? "–");
        await eventBus.PublishAsync(
            new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, role, status, detail),
            CancellationToken.None).ConfigureAwait(false);
    }

    private ValueTask<string?> GetTokenAsync(PlatformAuthRole role, string contextId, CancellationToken ct)
    {
        return AuthContextIds.IsDefault(contextId)
            ? authService.GetAccessTokenAsync(role, forceRefresh: false, ct)
            : authService.GetAccessTokenAsync(role, contextId, forceRefresh: false, ct);
    }

    private async Task<YouTubeVideoLiveSample?> GetVideoLiveSampleForRoleAsync(
        string videoId,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken ct)
    {
        if (role == PlatformAuthRole.Broadcaster)
        {
            if (AuthContextIds.IsDefault(contextId))
            {
                using var _ = PlatformAuthRequestOptions.UseContext(AuthContextIds.Default);
                return await broadcastClient.GetVideoLiveSampleAsync(videoId, ct).ConfigureAwait(false);
            }

            return await broadcastClient.GetVideoLiveSampleAsync(videoId, contextId, ct).ConfigureAwait(false);
        }

        return await broadcastClient.GetVideoLiveSampleAsync(videoId, role, contextId, ct).ConfigureAwait(false);
    }

    private static YouTubeMonitoringTransport ResolveMonitoringTransport(PlatformAuthRole role, bool isActiveBroadcast)
    {
        if (role == PlatformAuthRole.Broadcaster)
        {
            return YouTubeMonitoringTransport.DataApi;
        }

        return isActiveBroadcast
            ? YouTubeMonitoringTransport.HybridInnerTubeAndDataApi
            : YouTubeMonitoringTransport.InnerTubeDiscovery;
    }

    private static MonitoringUnavailableReason MapIssueToReason(YouTubeMonitorIssue issue)
    {
        return issue switch
        {
            YouTubeMonitorIssue.MissingBroadcasterAuth => MonitoringUnavailableReason.MissingBroadcasterAuth,
            YouTubeMonitorIssue.MissingBotAuth => MonitoringUnavailableReason.MissingBotAuth,
            YouTubeMonitorIssue.InvalidTargetChannel => MonitoringUnavailableReason.InvalidChannel,
            YouTubeMonitorIssue.QuotaExhausted => MonitoringUnavailableReason.QuotaExhausted,
            YouTubeMonitorIssue.MultipleActiveBroadcasts => MonitoringUnavailableReason.MultipleActiveBroadcasts,
            YouTubeMonitorIssue.TransientError => MonitoringUnavailableReason.TransportDisconnected,
            YouTubeMonitorIssue.AccessUnverified => MonitoringUnavailableReason.AccessUnverified,
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

        public PlatformAuthRole? ActiveDiscoveryRole { get; set; }

        public string? ActiveDiscoveryContextId { get; set; }

        public IReadOnlyList<YouTubeBroadcastSummary> ActiveCandidates { get; set; } = [];

        public ChannelMonitoringAvailability Availability { get; set; } = ChannelMonitoringAvailability.Active;

        public MonitoringUnavailableReason Reason { get; set; }

        public string? Detail { get; set; }

        public DateTimeOffset? LastPollAt { get; set; }

        public DateTimeOffset? LastDiscoveryAtUtc { get; set; }

        public DateTimeOffset? LastSampleAtUtc { get; set; }

        public DateTimeOffset? NextRetryAtUtc { get; set; }

        public int ConsecutiveFailures { get; set; }

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

    private bool IsStillActiveChannel(
        string channelId,
        PlatformChannelRoutingSettings routing,
        IReadOnlyList<ChannelCatalogEntry> entries)
    {
        if (!string.Equals(channelId, ResolveActiveChannelId(routing, entries), StringComparison.Ordinal))
        {
            return false;
        }

        lock (_sync)
        {
            return !_disposed && string.Equals(_targetChannelId, channelId, StringComparison.Ordinal);
        }
    }

    private bool IsStillActiveChannelLocked(
        string channelId,
        PlatformChannelRoutingSettings routing,
        IReadOnlyList<ChannelCatalogEntry> entries)
    {
        return !_disposed
            && string.Equals(_targetChannelId, channelId, StringComparison.Ordinal)
            && string.Equals(ResolveActiveChannelId(routing, entries), channelId, StringComparison.Ordinal);
    }

    private bool CanMutateActiveChannelLocked(
        string channelId,
        PlatformChannelRoutingSettings? routing = null,
        IReadOnlyList<ChannelCatalogEntry>? entries = null,
        long? expectedSettingsRevision = null)
    {
        if (_disposed || !string.Equals(_targetChannelId, channelId, StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedSettingsRevision is { } rev && Volatile.Read(ref _settingsRevision) != rev)
        {
            return false;
        }

        if (routing is not null && entries is not null)
        {
            return string.Equals(ResolveActiveChannelId(routing, entries), channelId, StringComparison.Ordinal);
        }

        return true;
    }

    private string ResolveActiveChannelId(PlatformChannelRoutingSettings routing, IReadOnlyList<ChannelCatalogEntry> entries)
    {
        return channelRegistry?.GetBinding(StreamingPlatform.YouTube)?.Key.ChannelId
            ?? channelCatalogStore?.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId
            ?? routing.ActiveChannelId
            ?? entries[0].ChannelId;
    }
}
