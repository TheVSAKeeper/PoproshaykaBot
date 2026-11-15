using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
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
    ILogger<YouTubeStreamMonitor> logger)
    : IStreamStatus, IStreamHostedComponent, IYouTubeActiveBroadcastSource, IYouTubeMonitorDiagnostics, IDisposable
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

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _waitCts;
    private Task? _loopTask;
    private IDisposable? _authSubscription;

    private bool _enabled;
    private YouTubeMonitorIssue _issue;
    private string? _targetChannelId;
    private string? _lastPublishedChannelId;
    private bool _publishedOnline;
    private bool _initialStatusResolved;
    private YouTubeBroadcastSummary? _activeBroadcast;
    private IReadOnlyList<YouTubeBroadcastSummary> _activeCandidates = [];
    private string? _lastLoggedMultiSetKey;
    private DateTimeOffset? _lastPollAt;
    private int _consecutiveFailures;
    private bool _wakeSubscriptionsAttached;
    private bool _disposed;

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

    public StreamStatusSnapshot GetSnapshot() => _stateMachine.GetSnapshot();

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_loopTask is not null)
            {
                return Task.CompletedTask;
            }

            _runCts = new();
            _authSubscription ??= eventBus.Subscribe<PlatformAuthorizationRefreshed>(OnAuthorizationRefreshed);
            if (!_wakeSubscriptionsAttached)
            {
                settingsStore.Changed += Wake;
                selectionStore.SelectionChanged += Wake;
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
        var detachWake = false;

        lock (_sync)
        {
            runCts = _runCts;
            _runCts = null;
            loopTask = _loopTask;
            _loopTask = null;
            authSub = _authSubscription;
            _authSubscription = null;
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
        }

        authSub?.Dispose();

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
    }

    public Task RefreshLiveSnapshotAsync()
    {
        return RefreshLiveSnapshotAsync(CancellationToken.None);
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
        }

        authSub?.Dispose();
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

            lock (_sync)
            {
                _targetChannelId = targetChannelId;
                active = _activeBroadcast;
            }

            if (active is null || !string.Equals(active.ChannelId, targetChannelId, StringComparison.Ordinal))
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
            return HandleTransientFailure(ex, YouTubeMonitorIssue.TransientError);
        }
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

        var selected = SelectBroadcast(targetChannelId, previousActive, matches);

        if (selected is null)
        {
            await HandleAmbiguousBroadcastsAsync(targetChannelId, matches, previousActive).ConfigureAwait(false);
            return OfflinePollInterval;
        }

        await ActivateBroadcastAsync(targetChannelId, selected, matches, previousActive).ConfigureAwait(false);
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
                _activeCandidates = [];
                _issue = YouTubeMonitorIssue.None;
                _lastLoggedMultiSetKey = null;
            }

            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);

            await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);

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
                    active.VideoId),
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

        return OnlinePollInterval;
    }

    private YouTubeBroadcastSummary? SelectBroadcast(
        string targetChannelId,
        YouTubeBroadcastSummary? previousActive,
        YouTubeBroadcastSummary[] matches)
    {
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (previousActive is not null)
        {
            var pinned = matches.FirstOrDefault(m => string.Equals(m.VideoId, previousActive.VideoId, StringComparison.Ordinal));

            if (pinned is not null)
            {
                return pinned;
            }
        }

        var selectedVideoId = selectionStore.GetSelectedVideoId(targetChannelId);

        if (!string.IsNullOrWhiteSpace(selectedVideoId))
        {
            return matches.FirstOrDefault(m => string.Equals(m.VideoId, selectedVideoId, StringComparison.Ordinal));
        }

        return null;
    }

    private async Task ActivateBroadcastAsync(
        string targetChannelId,
        YouTubeBroadcastSummary selected,
        YouTubeBroadcastSummary[] matches,
        YouTubeBroadcastSummary? previousActive)
    {
        var streamInfo = BuildStreamInfo(selected, targetChannelId, selected.Title, selected.StartedAtUtc.UtcDateTime, 0);
        var transition = _stateMachine.ApplyOnlineWithoutViewerSample(streamInfo);

        lock (_sync)
        {
            _activeBroadcast = selected;
            _activeCandidates = matches;
            _issue = YouTubeMonitorIssue.None;
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive?.VideoId != selected.VideoId)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(selected), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishOnlineOrMetadataAsync(transition, targetChannelId).ConfigureAwait(false);
    }

    private async Task HandleNoActiveBroadcastsAsync(string targetChannelId, YouTubeBroadcastSummary? previousActive)
    {
        var transition = _stateMachine.ApplyOffline();

        lock (_sync)
        {
            _activeBroadcast = null;
            _activeCandidates = [];
            _issue = YouTubeMonitorIssue.None;
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

        await PublishOfflineIfNeededAsync(transition, targetChannelId).ConfigureAwait(false);
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
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }
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
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }
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
            _activeCandidates = [];
            _lastLoggedMultiSetKey = null;
        }

        if (previousActive is not null)
        {
            await eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None).ConfigureAwait(false);
        }

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
                new StreamWentOnline(StreamingPlatform.YouTube, targetChannelId, _stateMachine.CurrentStream, IsCatchUp: false),
                CancellationToken.None).ConfigureAwait(false);
        }
        else if (transition.SnapshotChanged && _stateMachine.CurrentStream is { } updatedStream)
        {
            await eventBus.PublishAsync(
                new StreamMetadataResolved(StreamingPlatform.YouTube, targetChannelId, updatedStream),
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
                new StreamWentOffline(StreamingPlatform.YouTube, targetChannelId, IsCatchUp: isCatchUp),
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

    private void Wake()
    {
        CancellationTokenSource? waitCts;

        lock (_sync)
        {
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

    private static StreamInfo BuildStreamInfo(
        YouTubeBroadcastSummary broadcast,
        string targetChannelId,
        string title,
        DateTime startedAtUtc,
        int viewerCount)
    {
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
        };
    }
}
