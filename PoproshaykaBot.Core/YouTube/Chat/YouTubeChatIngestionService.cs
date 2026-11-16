using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class YouTubeChatIngestionService(
    IYouTubeLiveChatStreamClient streamClient,
    IYouTubeLiveChatPollingClient pollingClient,
    YouTubeChatMessageMapper mapper,
    IYouTubeActiveBroadcastSource streamMonitor,
    YouTubeSessionContext sessionContext,
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    YouTubeAccountsStore accountsStore,
    IYouTubeQuotaGovernor quotaGovernor,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<YouTubeChatIngestionService> logger,
    YouTubeSettingsStore? settingsStore = null,
    IYouTubeSentMessageTracker? sentMessageTracker = null)
    : IHostedComponent, IYouTubeChatDiagnostics, IDisposable
{
    private const int MaxHeldEchoCandidates = 32;
    private static readonly TimeSpan MaxEchoHoldDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WaitingBroadcastInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumStreamReconnectFloor = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RapidCloseThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinimumRestInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestProbeInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan QuotaWaitCheckCap = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan[] RapidCloseDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];
    private static readonly TimeSpan[] TransientBackoffSteps =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    private readonly IYouTubeSentMessageTracker _sentMessageTracker =
        sentMessageTracker ?? NullYouTubeSentMessageTracker.Instance;
    private readonly Lock _sync = new();
    private readonly Lock _holdSync = new();
    private readonly Queue<HeldEchoCandidate> _heldCandidates = new();

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _waitCts;
    private CancellationTokenSource? _activeStreamCts;
    private CancellationTokenSource? _holdDrainCts;
    private CancellationToken _holdParentCt;
    private Task? _loopTask;
    private Task? _holdDrainTask;
    private IDisposable? _broadcastSub;
    private IDisposable? _authSub;
    private bool _settingsSubscribed;
    private bool _ingestionPublished;

    private YouTubeChatSessionCursor? _cursor;
    private ConnectionState _state = ConnectionState.Disconnected;
    private YouTubeChatTransport _transport;
    private YouTubeChatIssue _issue;
    private string? _channel;
    private string? _liveChatId;
    private string? _activeStreamLiveChatId;
    private DateTimeOffset? _connectedSince;
    private DateTimeOffset? _lastBatchAt;
    private DateTimeOffset? _lastMessageAt;
    private long _receivedMessages;
    private int _gapCount;
    private int _reconnectAttempt;

    private bool _usingRestFallback;
    private DateTimeOffset? _restProbeAtUtc;
    private int _consecutiveGrpcTransportFailures;
    private int _consecutiveRapidCloses;
    private int _consecutiveTransientFailures;
    private bool _wakePending;
    private bool _isRunning;
    private bool _disposed;

    public string Name => "Чтение чата YouTube";

    public int StartOrder => 300;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isRunning && _state == ConnectionState.Connected;
            }
        }
    }

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed || _loopTask is not null)
            {
                return Task.CompletedTask;
            }

            _runCts = new();
            _wakePending = false;
            _isRunning = true;
            _broadcastSub ??= eventBus.Subscribe<YouTubeActiveBroadcastChanged>(OnActiveBroadcastChanged);
            _authSub ??= eventBus.Subscribe<PlatformAuthorizationRefreshed>(OnAuthorizationRefreshed);

            if (settingsStore is not null && !_settingsSubscribed)
            {
                settingsStore.Changed += Wake;
                _settingsSubscribed = true;
            }

            _loopTask = RunLoopAsync(_runCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource? runCts;
        Task? loopTask;
        IDisposable? broadcastSub;
        IDisposable? authSub;
        bool unsubscribeSettings;

        lock (_sync)
        {
            runCts = _runCts;
            _runCts = null;
            loopTask = _loopTask;
            _loopTask = null;
            broadcastSub = _broadcastSub;
            _broadcastSub = null;
            authSub = _authSub;
            _authSub = null;
            unsubscribeSettings = _settingsSubscribed;
            _settingsSubscribed = false;
            _isRunning = false;
            _state = ConnectionState.Disconnected;
            _transport = YouTubeChatTransport.None;
            _issue = YouTubeChatIssue.None;
            _connectedSince = null;
        }

        if (unsubscribeSettings && settingsStore is not null)
        {
            settingsStore.Changed -= Wake;
        }

        broadcastSub?.Dispose();
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
        await ClearAndAwaitHoldDrainAsync().ConfigureAwait(false);
        await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
    }

    public YouTubeChatStatus CaptureDiagnostics()
    {
        lock (_sync)
        {
            return new(
                _state,
                _transport,
                _issue,
                _channel,
                _liveChatId,
                _connectedSince,
                _lastBatchAt,
                _lastMessageAt,
                _receivedMessages,
                _gapCount,
                _reconnectAttempt);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? runCts;
        CancellationTokenSource? waitCts;
        CancellationTokenSource? activeStreamCts;
        IDisposable? broadcastSub;
        IDisposable? authSub;
        bool unsubscribeSettings;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isRunning = false;
            runCts = _runCts;
            _runCts = null;
            waitCts = _waitCts;
            activeStreamCts = _activeStreamCts;
            broadcastSub = _broadcastSub;
            _broadcastSub = null;
            authSub = _authSub;
            _authSub = null;
            unsubscribeSettings = _settingsSubscribed;
            _settingsSubscribed = false;
        }

        if (unsubscribeSettings && settingsStore is not null)
        {
            settingsStore.Changed -= Wake;
        }

        broadcastSub?.Dispose();
        authSub?.Dispose();

        if (runCts is not null)
        {
            try
            {
                runCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            runCts.Dispose();
        }

        if (activeStreamCts is not null)
        {
            try
            {
                activeStreamCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (waitCts is not null)
        {
            try
            {
                waitCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        CancellationTokenSource? holdDrainCts;

        lock (_holdSync)
        {
            _heldCandidates.Clear();
            holdDrainCts = _holdDrainCts;
            _holdDrainCts = null;
        }

        if (holdDrainCts is not null)
        {
            try
            {
                holdDrainCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            holdDrainCts.Dispose();
        }
    }

    internal async Task<TimeSpan> RunSingleIterationAsync(CancellationToken ct)
    {
        var botToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, false, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(botToken))
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);

            lock (_sync)
            {
                _state = ConnectionState.Disconnected;
                _issue = YouTubeChatIssue.MissingBotAuth;
                _transport = YouTubeChatTransport.None;
                _connectedSince = null;
            }

            return WaitingBroadcastInterval;
        }

        var broadcast = streamMonitor.CurrentBroadcast;

        if (broadcast is null || string.IsNullOrWhiteSpace(broadcast.LiveChatId))
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);

            lock (_sync)
            {
                _liveChatId = null;
                _connectedSince = null;
                _transport = YouTubeChatTransport.None;
                _state = ConnectionState.Connecting;
                _issue = YouTubeChatIssue.WaitingForActiveBroadcast;
                _channel = sessionContext.Current?.ChannelLabel;
            }

            return WaitingBroadcastInterval;
        }

        var channelLabel = sessionContext.Current?.ChannelLabel;

        if (string.IsNullOrWhiteSpace(channelLabel))
        {
            channelLabel = broadcast.ChannelId;
        }

        YouTubeChatSessionCursor cursor;
        bool useRest;
        bool switchedChat;

        lock (_sync)
        {
            switchedChat = _cursor is not null
                && !string.Equals(_cursor.LiveChatId, broadcast.LiveChatId, StringComparison.Ordinal);

            if (_cursor is null || switchedChat)
            {
                _cursor = new(broadcast.LiveChatId, timeProvider.GetUtcNow());
                _connectedSince = null;
            }

            cursor = _cursor;
            _channel = channelLabel;
            _liveChatId = broadcast.LiveChatId;

            if (_usingRestFallback && _restProbeAtUtc.HasValue && timeProvider.GetUtcNow() >= _restProbeAtUtc.Value)
            {
                _usingRestFallback = false;
                _restProbeAtUtc = null;
                _consecutiveGrpcTransportFailures = 0;
            }

            useRest = _usingRestFallback;
        }

        if (switchedChat)
        {
            await ClearAndAwaitHoldDrainAsync().ConfigureAwait(false);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
        }

        using var streamCts = CreateActiveStreamCts(broadcast.LiveChatId, ct);

        if (streamCts is null)
        {
            return TimeSpan.Zero;
        }

        try
        {
            if (!IsBroadcastCurrent(broadcast.LiveChatId))
            {
                await ResetInactiveStateAsync(ct).ConfigureAwait(false);
                return TimeSpan.Zero;
            }

            return useRest
                ? await ExecuteRestPollAsync(broadcast, cursor, channelLabel, streamCts.Token, ct).ConfigureAwait(false)
                : await ExecuteGrpcStreamAsync(broadcast, cursor, channelLabel, streamCts.Token, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await ResetInactiveStateAsync(ct).ConfigureAwait(false);
            return TimeSpan.Zero;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.InvalidPageToken)
        {
            return await HandleInvalidPageTokenAsync(cursor, channelLabel, broadcast.LiveChatId, ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.LiveChatEnded)
        {
            logger.LogInformation(ex, "YouTube чат {LiveChatId} завершён, обновляем статус трансляции", broadcast.LiveChatId);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
            await streamMonitor.RefreshLiveSnapshotAsync(ct).ConfigureAwait(false);
            return MinimumStreamReconnectFloor;
        }
        catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached)
        {
            return await HandleQuotaExceededAsync(ex.Kind == YouTubeApiErrorKind.QuotaExceeded, ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.TransportUnsupported)
        {
            SwitchToRestFallback("транспорт gRPC StreamList не поддерживается");
            return TimeSpan.FromSeconds(1);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
            return HandleTransientFailure(ex, YouTubeChatIssue.MissingBotAuth, countTowardsGrpcFallback: false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.RateLimitExceeded)
        {
            return HandleTransientFailure(ex, YouTubeChatIssue.TransientError, countTowardsGrpcFallback: false);
        }
        catch (Exception ex) when (ex is YouTubeApiException or HttpRequestException)
        {
            return HandleTransientFailure(ex, YouTubeChatIssue.TransientError, countTowardsGrpcFallback: !useRest);
        }
        finally
        {
            ClearActiveStreamCts(streamCts);
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;

            lock (_sync)
            {
                _wakePending = false;
            }

            try
            {
                delay = await RunSingleIterationAsync(ct).ConfigureAwait(false);
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
                delay = TransientBackoffSteps[0];
                logger.LogWarning(ex, "YouTubeChatIngestionService: непредвиденная ошибка цикла чтения");
            }

            if (delay > TimeSpan.Zero)
            {
                await DelayInterruptibleAsync(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<TimeSpan> ExecuteGrpcStreamAsync(
        YouTubeBroadcastSummary broadcast,
        YouTubeChatSessionCursor cursor,
        string channelLabel,
        CancellationToken streamCt,
        CancellationToken loopCt)
    {
        var openedAt = timeProvider.GetUtcNow();
        TimeSpan lastBatchPolling = MinimumStreamReconnectFloor;
        var sawChatEnded = false;
        var startedPublished = false;

        lock (_sync)
        {
            _state = ConnectionState.Connected;
            _transport = YouTubeChatTransport.GrpcStream;
            _issue = YouTubeChatIssue.None;
            _connectedSince ??= openedAt;
        }

        await foreach (var batch in streamClient.StreamBatchesAsync(broadcast.LiveChatId, cursor.PageToken, streamCt).ConfigureAwait(false))
        {
            if (!IsBroadcastCurrent(broadcast.LiveChatId))
            {
                await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
                return TimeSpan.Zero;
            }

            if (!startedPublished)
            {
                startedPublished = true;
                await MarkIngestionStartedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);
            }

            lastBatchPolling = batch.PollingInterval > MinimumStreamReconnectFloor
                ? batch.PollingInterval
                : MinimumStreamReconnectFloor;

            lock (_sync)
            {
                _consecutiveGrpcTransportFailures = 0;
                _consecutiveTransientFailures = 0;
                _reconnectAttempt = 0;
            }

            var batchOutcome = await ProcessBatchAsync(cursor, batch, channelLabel, broadcast.ChannelId, loopCt).ConfigureAwait(false);

            if (batchOutcome.AbortedByBroadcastChange)
            {
                await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
                return TimeSpan.Zero;
            }

            if (batchOutcome.SawChatEnded)
            {
                sawChatEnded = true;
                break;
            }
        }

        if (!IsBroadcastCurrent(broadcast.LiveChatId))
        {
            await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
            return TimeSpan.Zero;
        }

        if (sawChatEnded)
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);
            await streamMonitor.RefreshLiveSnapshotAsync(loopCt).ConfigureAwait(false);
            return MinimumStreamReconnectFloor;
        }

        var elapsed = timeProvider.GetUtcNow() - openedAt;
        int rapidCloses;

        lock (_sync)
        {
            _consecutiveTransientFailures = 0;
            _reconnectAttempt = 0;

            if (elapsed < RapidCloseThreshold)
            {
                _consecutiveRapidCloses++;
            }
            else
            {
                _consecutiveRapidCloses = 0;
            }

            rapidCloses = _consecutiveRapidCloses;
        }

        if (rapidCloses >= 3)
        {
            var index = Math.Clamp(rapidCloses - 3, 0, RapidCloseDelays.Length - 1);
            return RapidCloseDelays[index];
        }

        var remainingFloor = MinimumStreamReconnectFloor - elapsed;
        return remainingFloor > TimeSpan.Zero ? remainingFloor : lastBatchPolling;
    }

    private async Task<TimeSpan> ExecuteRestPollAsync(
        YouTubeBroadcastSummary broadcast,
        YouTubeChatSessionCursor cursor,
        string channelLabel,
        CancellationToken streamCt,
        CancellationToken loopCt)
    {
        lock (_sync)
        {
            _state = ConnectionState.Connected;
            _transport = YouTubeChatTransport.RestPolling;
            _issue = YouTubeChatIssue.None;
            _connectedSince ??= timeProvider.GetUtcNow();
        }

        var batch = await pollingClient.ListMessagesAsync(broadcast.LiveChatId, cursor.PageToken, streamCt).ConfigureAwait(false);

        if (!IsBroadcastCurrent(broadcast.LiveChatId))
        {
            await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
            return TimeSpan.Zero;
        }

        await MarkIngestionStartedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);

        lock (_sync)
        {
            _consecutiveTransientFailures = 0;
            _reconnectAttempt = 0;
        }

        var batchOutcome = await ProcessBatchAsync(cursor, batch, channelLabel, broadcast.ChannelId, loopCt).ConfigureAwait(false);

        if (batchOutcome.AbortedByBroadcastChange)
        {
            await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
            return TimeSpan.Zero;
        }

        if (batchOutcome.SawChatEnded)
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);
            await streamMonitor.RefreshLiveSnapshotAsync(loopCt).ConfigureAwait(false);
            return MinimumRestInterval;
        }

        return batch.PollingInterval > MinimumRestInterval ? batch.PollingInterval : MinimumRestInterval;
    }

    private async Task<BatchProcessOutcome> ProcessBatchAsync(
        YouTubeChatSessionCursor cursor,
        YouTubeLiveChatBatch batch,
        string channelLabel,
        string broadcastChannelId,
        CancellationToken ct)
    {
        if (!IsBroadcastCurrent(cursor.LiveChatId))
        {
            return new(SawChatEnded: false, AbortedByBroadcastChange: true);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var batchResult = cursor.FilterBatch(batch);

        lock (_sync)
        {
            _lastBatchAt = nowUtc;
        }

        var session = sessionContext.Current;
        var botChannelId = session?.BotChannelId;
        var broadcasterChannelId = string.IsNullOrWhiteSpace(session?.TargetChannelId)
            ? broadcastChannelId
            : session.TargetChannelId;

        if (string.IsNullOrWhiteSpace(botChannelId))
        {
            botChannelId = accountsStore.Load(PlatformAuthRole.Bot).ChannelId;
        }

        var sawChatEnded = false;

        foreach (var item in batchResult.HistoricalItems)
        {
            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                sawChatEnded = true;
            }

            cursor.MarkSeen(item.Id);
        }

        foreach (var item in batchResult.LiveItems)
        {
            if (!IsBroadcastCurrent(cursor.LiveChatId))
            {
                DropHeldCandidatesForChat(cursor.LiveChatId);
                return new(sawChatEnded, AbortedByBroadcastChange: true);
            }

            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                sawChatEnded = true;
                cursor.MarkSeen(item.Id);
                continue;
            }

            var messageTimestampUtc = item.PublishedAtUtc ?? nowUtc;

            if (TryHoldSharedAccountCandidate(
                    cursor.LiveChatId,
                    item,
                    channelLabel,
                    botChannelId,
                    broadcasterChannelId,
                    messageTimestampUtc,
                    nowUtc,
                    ct,
                    out var overflowCandidate))
            {
                if (overflowCandidate is not null && IsBroadcastCurrent(overflowCandidate.LiveChatId))
                {
                    await PublishItemAsync(
                        overflowCandidate.Item,
                        overflowCandidate.ChannelLabel,
                        overflowCandidate.BotChannelId,
                        overflowCandidate.BroadcasterChannelId,
                        overflowCandidate.MessageTimestampUtc,
                        ct).ConfigureAwait(false);
                }

                cursor.MarkSeen(item.Id);
                continue;
            }

            await PublishItemAsync(
                item,
                channelLabel,
                botChannelId,
                broadcasterChannelId,
                messageTimestampUtc,
                ct).ConfigureAwait(false);

            cursor.MarkSeen(item.Id);
        }

        if (!IsBroadcastCurrent(cursor.LiveChatId))
        {
            return new(sawChatEnded, AbortedByBroadcastChange: true);
        }

        cursor.CompleteBatch(batch.NextPageToken, batchResult.LiveItems.Count > 0);
        return new(sawChatEnded, AbortedByBroadcastChange: false);
    }

    private async Task<TimeSpan> HandleInvalidPageTokenAsync(
        YouTubeChatSessionCursor cursor,
        string channelLabel,
        string liveChatId,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow();

        lock (_sync)
        {
            _gapCount++;
        }

        cursor.ResetAfterInvalidToken(nowUtc);

        logger.LogWarning(
            "YouTube чат {LiveChatId}: курсор сообщений устарел, возможен пропуск старых сообщений при пересинхронизации",
            liveChatId);

        await eventBus.PublishAsync(
            new ChatReadingGapDetected(StreamingPlatform.YouTube, channelLabel, liveChatId, nowUtc),
            ct).ConfigureAwait(false);

        return MinimumStreamReconnectFloor;
    }

    private async Task<TimeSpan> HandleQuotaExceededAsync(bool exhaustedByServer, CancellationToken ct)
    {
        if (exhaustedByServer)
        {
            quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Bot);
        }

        await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);

        lock (_sync)
        {
            _state = ConnectionState.Disconnected;
            _transport = YouTubeChatTransport.None;
            _issue = YouTubeChatIssue.QuotaExhausted;
            _connectedSince = null;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var window = PacificQuotaClock.GetWindow(nowUtc);
        var remaining = window.NextResetAtUtc - nowUtc;

        if (remaining <= TimeSpan.Zero)
        {
            return MinimumRestInterval;
        }

        return remaining > QuotaWaitCheckCap ? QuotaWaitCheckCap : remaining;
    }

    private Task MarkIngestionStartedAsync(DateTimeOffset at, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_ingestionPublished)
            {
                return Task.CompletedTask;
            }

            _ingestionPublished = true;
        }

        return eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.YouTube, at), ct);
    }

    private Task MarkIngestionStoppedAsync(DateTimeOffset at, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_ingestionPublished)
            {
                return Task.CompletedTask;
            }

            _ingestionPublished = false;
        }

        return eventBus.PublishAsync(new ChatIngestionStopped(StreamingPlatform.YouTube, at), ct);
    }

    private void SwitchToRestFallback(string reason)
    {
        lock (_sync)
        {
            _usingRestFallback = true;
            _restProbeAtUtc = timeProvider.GetUtcNow() + RestProbeInterval;
            _consecutiveGrpcTransportFailures = 0;
        }

        logger.LogWarning("YouTube чат: переключение на REST polling ({Reason}); повторная проба gRPC через 10 мин", reason);
    }

    private TimeSpan HandleTransientFailure(Exception exception, YouTubeChatIssue issue, bool countTowardsGrpcFallback)
    {
        var switchedToRest = false;
        int failures;

        lock (_sync)
        {
            _consecutiveTransientFailures++;
            _reconnectAttempt++;
            failures = _consecutiveTransientFailures;
            _state = ConnectionState.Reconnecting;
            _issue = issue;

            if (countTowardsGrpcFallback)
            {
                _consecutiveGrpcTransportFailures++;

                if (_consecutiveGrpcTransportFailures >= 2)
                {
                    _usingRestFallback = true;
                    _restProbeAtUtc = timeProvider.GetUtcNow() + RestProbeInterval;
                    _consecutiveGrpcTransportFailures = 0;
                    switchedToRest = true;
                }
            }
        }

        if (switchedToRest)
        {
            logger.LogWarning(
                exception,
                "YouTube чат: после серии сбоев gRPC включается резервный REST polling на 10 мин");
            return MinimumStreamReconnectFloor;
        }

        var index = Math.Clamp(failures - 1, 0, TransientBackoffSteps.Length - 1);
        var delay = TransientBackoffSteps[index];

        logger.LogWarning(
            exception,
            "YouTubeChatIngestionService: сбой чтения чата ({Issue}, попытка #{Failures}), повтор через {DelaySeconds} с",
            issue,
            failures,
            (int)delay.TotalSeconds);

        return delay;
    }

    private void OnActiveBroadcastChanged(YouTubeActiveBroadcastChanged @event)
    {
        CancellationTokenSource? streamCts = null;

        lock (_sync)
        {
            if (_activeStreamCts is not null
                && (@event.ActiveBroadcast is null
                    || !string.Equals(@event.ActiveBroadcast.LiveChatId, _activeStreamLiveChatId, StringComparison.Ordinal)))
            {
                streamCts = _activeStreamCts;
            }
        }

        if (streamCts is not null)
        {
            try
            {
                streamCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        Wake();
    }

    private CancellationTokenSource? CreateActiveStreamCts(string liveChatId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _activeStreamCts = cts;
            _activeStreamLiveChatId = liveChatId;
            return cts;
        }
    }

    private void ClearActiveStreamCts(CancellationTokenSource streamCts)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeStreamCts, streamCts))
            {
                _activeStreamCts = null;
                _activeStreamLiveChatId = null;
            }
        }
    }

    private bool IsBroadcastCurrent(string liveChatId)
    {
        var current = streamMonitor.CurrentBroadcast;
        return current is not null && string.Equals(current.LiveChatId, liveChatId, StringComparison.Ordinal);
    }

    private async Task ResetInactiveStateAsync(CancellationToken ct)
    {
        await ClearAndAwaitHoldDrainAsync().ConfigureAwait(false);
        await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);

        lock (_sync)
        {
            _state = ConnectionState.Disconnected;
            _transport = YouTubeChatTransport.None;
            _connectedSince = null;
        }
    }

    private void OnAuthorizationRefreshed(PlatformAuthorizationRefreshed @event)
    {
        if (@event.Platform == StreamingPlatform.YouTube && @event.Role == PlatformAuthRole.Bot)
        {
            Wake();
        }
    }

    private void Wake()
    {
        CancellationTokenSource? waitCts;

        lock (_sync)
        {
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
        CancellationTokenSource? waitCts;

        lock (_sync)
        {
            if (_disposed || ct.IsCancellationRequested || _wakePending)
            {
                _wakePending = false;
                return;
            }

            try
            {
                waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _waitCts = waitCts;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
        }

        try
        {
            await Task.Delay(delay, timeProvider, waitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested || _disposed)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        finally
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

    private bool TryHoldSharedAccountCandidate(
        string liveChatId,
        YouTubeLiveChatItem item,
        string channelLabel,
        string? botChannelId,
        string? broadcasterChannelId,
        DateTimeOffset messageTimestampUtc,
        DateTimeOffset nowUtc,
        CancellationToken ct,
        out HeldEchoCandidate? overflowCandidate)
    {
        overflowCandidate = null;

        if (item.Kind != YouTubeLiveChatItemKind.TextMessage
            || string.IsNullOrWhiteSpace(botChannelId)
            || !string.Equals(item.Author.ChannelId, botChannelId, StringComparison.Ordinal))
        {
            return false;
        }

        var isSharedAccount = string.Equals(botChannelId, broadcasterChannelId, StringComparison.Ordinal)
            || item.Author.IsChatOwner;

        if (!isSharedAccount || _sentMessageTracker.Contains(item.Id))
        {
            return false;
        }

        var watermarkSeq = _sentMessageTracker.CaptureInFlightWatermark(liveChatId);

        lock (_holdSync)
        {
            var hasPendingForChat = HasHeldCandidatesForChatLocked(liveChatId);

            if (watermarkSeq == 0 && !hasPendingForChat)
            {
                return false;
            }

            if (_heldCandidates.Count >= MaxHeldEchoCandidates)
            {
                overflowCandidate = _heldCandidates.Dequeue();
            }

            _heldCandidates.Enqueue(new(
                liveChatId,
                item,
                channelLabel,
                botChannelId,
                broadcasterChannelId,
                watermarkSeq,
                messageTimestampUtc,
                nowUtc + MaxEchoHoldDuration));

            EnsureHoldDrainRunningLocked(ct);
            return true;
        }
    }

    private bool HasHeldCandidatesForChatLocked(string liveChatId)
    {
        foreach (var candidate in _heldCandidates)
        {
            if (string.Equals(candidate.LiveChatId, liveChatId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureHoldDrainRunningLocked(CancellationToken parentCt)
    {
        if (_holdDrainTask is not null && !_holdDrainTask.IsCompleted)
        {
            return;
        }

        _holdParentCt = parentCt;
        _holdDrainCts?.Dispose();
        var drainCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        _holdDrainCts = drainCts;
        _holdDrainTask = Task.Run(() => DrainHeldCandidatesAsync(drainCts), CancellationToken.None);
    }

    private async Task DrainHeldCandidatesAsync(CancellationTokenSource drainCts)
    {
        var holdCt = drainCts.Token;

        try
        {
            while (!holdCt.IsCancellationRequested)
            {
                HeldEchoCandidate? head;

                lock (_holdSync)
                {
                    if (!_heldCandidates.TryPeek(out head))
                    {
                        return;
                    }
                }

                if (!IsBroadcastCurrent(head.LiveChatId))
                {
                    DropHeldCandidatesForChat(head.LiveChatId);
                    continue;
                }

                if (head.WatermarkSeq > 0 && !_sentMessageTracker.Contains(head.Item.Id))
                {
                    var remaining = head.ExpiresAtUtc - timeProvider.GetUtcNow();

                    if (remaining > TimeSpan.Zero)
                    {
                        await _sentMessageTracker.WaitUntilCompletedOrMatchedAsync(
                            head.LiveChatId,
                            head.WatermarkSeq,
                            head.Item.Id,
                            remaining,
                            timeProvider,
                            holdCt).ConfigureAwait(false);
                    }
                }

                HeldEchoCandidate? released = null;

                lock (_holdSync)
                {
                    if (_heldCandidates.TryPeek(out var currentHead) && ReferenceEquals(currentHead, head))
                    {
                        released = _heldCandidates.Dequeue();
                    }
                }

                if (released is not null && !holdCt.IsCancellationRequested && IsBroadcastCurrent(released.LiveChatId))
                {
                    await PublishItemAsync(
                        released.Item,
                        released.ChannelLabel,
                        released.BotChannelId,
                        released.BroadcasterChannelId,
                        released.MessageTimestampUtc,
                        holdCt).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (holdCt.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTubeChatIngestionService: ошибка в буфере согласования эха");
        }
        finally
        {
            lock (_holdSync)
            {
                if (ReferenceEquals(_holdDrainCts, drainCts))
                {
                    _holdDrainTask = null;

                    if (_heldCandidates.Count > 0 && !_holdParentCt.IsCancellationRequested && !_disposed)
                    {
                        EnsureHoldDrainRunningLocked(_holdParentCt);
                    }
                }
            }
        }
    }

    private async Task ClearAndAwaitHoldDrainAsync()
    {
        CancellationTokenSource? holdDrainCts;
        Task? holdDrainTask;

        lock (_holdSync)
        {
            _heldCandidates.Clear();
            holdDrainCts = _holdDrainCts;
            _holdDrainCts = null;
            holdDrainTask = _holdDrainTask;
            _holdDrainTask = null;
        }

        if (holdDrainCts is not null)
        {
            await holdDrainCts.CancelAsync().ConfigureAwait(false);
        }

        if (holdDrainTask is not null)
        {
            try
            {
                await holdDrainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        holdDrainCts?.Dispose();
    }

    private void DropHeldCandidatesForChat(string liveChatId)
    {
        lock (_holdSync)
        {
            if (_heldCandidates.Count == 0)
            {
                return;
            }

            var retained = _heldCandidates.Count;

            for (var i = 0; i < retained; i++)
            {
                var candidate = _heldCandidates.Dequeue();

                if (!string.Equals(candidate.LiveChatId, liveChatId, StringComparison.Ordinal))
                {
                    _heldCandidates.Enqueue(candidate);
                }
            }
        }
    }

    private async Task PublishItemAsync(
        YouTubeLiveChatItem item,
        string channelLabel,
        string? botChannelId,
        string? broadcasterChannelId,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var mapped = mapper.Map(item, channelLabel, botChannelId, broadcasterChannelId);

        if (mapped.Kind == YouTubeChatMapKind.ChatMessage && mapped.Message is not null)
        {
            await eventBus.PublishAsync(new RawChatMessageReceived(mapped.Message, timestamp), ct).ConfigureAwait(false);

            lock (_sync)
            {
                _lastMessageAt = timestamp;
                _receivedMessages++;
            }
        }
        else if (mapped.Kind == YouTubeChatMapKind.IgnoredMonetizationOrMembership)
        {
            logger.LogDebug(
                "YouTube чат: событие {EventType} ({Summary}) пропущено (не поддерживается обработчиком чата)",
                mapped.DiagnosticEventType,
                mapped.DiagnosticSummary ?? "-");
        }
    }

    private sealed record HeldEchoCandidate(
        string LiveChatId,
        YouTubeLiveChatItem Item,
        string ChannelLabel,
        string? BotChannelId,
        string? BroadcasterChannelId,
        long WatermarkSeq,
        DateTimeOffset MessageTimestampUtc,
        DateTimeOffset ExpiresAtUtc);

    private readonly record struct BatchProcessOutcome(bool SawChatEnded, bool AbortedByBroadcastChange);
}
