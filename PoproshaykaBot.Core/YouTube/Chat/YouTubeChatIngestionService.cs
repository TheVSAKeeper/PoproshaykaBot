using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class YouTubeChatIngestionService(
    IYouTubeLiveChatReader chatReader,
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

    private readonly CompositeYouTubeLiveChatReader? _compositeReader = chatReader as CompositeYouTubeLiveChatReader;
    private readonly IYouTubeSentMessageTracker _sentMessageTracker =
        sentMessageTracker ?? NullYouTubeSentMessageTracker.Instance;
    private readonly Lock _sync = new();
    private readonly Lock _holdSync = new();
    private readonly Queue<HeldEchoCandidate> _heldCandidates = new();

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _waitCts;
    private CancellationTokenSource? _activeStreamCts;
    private TaskCompletionSource? _activeReadTcs;
    private Task? _pendingIngestionStopTask;
    private long _ingestionStopGeneration;
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
    private ChatOrigin? _origin;
    private YouTubeBroadcastSummary? _originBroadcast;
    private string? _cursorChannelId;
    private YouTubeBroadcastSummary? _activeStreamBroadcast;
    private DateTimeOffset? _connectedSince;
    private DateTimeOffset? _lastBatchAt;
    private DateTimeOffset? _lastMessageAt;
    private long _receivedMessages;
    private int _gapCount;
    private int _reconnectAttempt;

    private int _consecutiveRapidCloses;
    private int _consecutiveTransientFailures;
    private bool _wakePending;
    private bool _isRunning;
    private bool _disposed;

    internal YouTubeChatIngestionService(
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
        : this(
            CreateOfficialCompositeReader(streamClient, pollingClient, timeProvider),
            mapper,
            streamMonitor,
            sessionContext,
            authService,
            accountsStore,
            quotaGovernor,
            eventBus,
            timeProvider,
            logger,
            settingsStore,
            sentMessageTracker)
    {
    }

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
            _broadcastSub ??= eventBus.Subscribe<YouTubeActiveBroadcastChanged>(OnActiveBroadcastChangedAsync);
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
            CloseOrigin();
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
            CloseOrigin();
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
        var broadcast = streamMonitor.CurrentBroadcast;

        if (broadcast is null
            || string.IsNullOrWhiteSpace(broadcast.ChannelId)
            || (string.IsNullOrWhiteSpace(broadcast.LiveChatId) && string.IsNullOrWhiteSpace(broadcast.VideoId)))
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

        var session = sessionContext.Current;
        var channelLabel = session?.ChannelLabel;

        if (string.IsNullOrWhiteSpace(channelLabel))
        {
            channelLabel = broadcast.ChannelId;
        }

        var botChannelId = session?.BotChannelId;

        if (string.IsNullOrWhiteSpace(botChannelId))
        {
            botChannelId = accountsStore.Load(PlatformAuthRole.Bot).ChannelId;
        }

        var broadcasterChannelId = broadcast.ChannelId;

        var isSharedAccount = !string.IsNullOrWhiteSpace(botChannelId)
            && !string.IsNullOrWhiteSpace(broadcasterChannelId)
            && string.Equals(botChannelId, broadcasterChannelId, StringComparison.Ordinal);

        var target = new YouTubeChatTarget(
            broadcast.VideoId,
            broadcast.LiveChatId,
            broadcasterChannelId,
            YouTubeChatMode.LiveChat,
            RequiresOfficialMessageIds: isSharedAccount);

        var broadcastKey = GetBroadcastKey(broadcast)!;

        YouTubeChatSessionCursor cursor;
        bool switchedChat;

        lock (_sync)
        {
            switchedChat = _cursor is not null
                && (!string.Equals(_cursor.LiveChatId, broadcastKey, StringComparison.Ordinal)
                    || !string.Equals(_cursorChannelId, broadcasterChannelId, StringComparison.Ordinal));

            if (_cursor is null || switchedChat)
            {
                _cursor = new(broadcastKey, timeProvider.GetUtcNow());
                _cursorChannelId = broadcasterChannelId;
                _connectedSince = null;
                _consecutiveRapidCloses = 0;
            }

            cursor = _cursor;
            _channel = channelLabel;
            _liveChatId = broadcastKey;
        }

        if (switchedChat)
        {
            await ClearAndAwaitHoldDrainAsync().ConfigureAwait(false);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
        }

        var selectedTransport = _compositeReader is not null
            ? _compositeReader.SelectTransport(target)
            : chatReader.TransportKind;

        if (selectedTransport == YouTubeChatTransportKind.None)
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);

            lock (_sync)
            {
                _connectedSince = null;
                _transport = YouTubeChatTransport.None;
                _state = ConnectionState.Connecting;
                _issue = YouTubeChatIssue.WaitingForActiveBroadcast;
            }

            return WaitingBroadcastInterval;
        }

        if (IsOfficialTransport(selectedTransport))
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
        }

        var streamScope = CreateActiveStreamScope(broadcast, ct);

        if (streamScope is null)
        {
            return TimeSpan.Zero;
        }

        using var streamCts = streamScope.Value.Cts;
        var shouldRefreshLiveSnapshot = false;
        TimeSpan resultDelay;

        try
        {
            if (!IsBroadcastCurrent(broadcast))
            {
                await ResetInactiveStateAsync(ct).ConfigureAwait(false);
                return TimeSpan.Zero;
            }

            var stepResult = await ExecuteTransportStepAsync(
                    target,
                    selectedTransport,
                    broadcast,
                    cursor,
                    channelLabel,
                    streamCts.Token,
                    ct)
                .ConfigureAwait(false);

            shouldRefreshLiveSnapshot = stepResult.RefreshSnapshot;
            resultDelay = stepResult.Delay;
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
        catch (InnerTubeChatException ex)
        {
            var outcome = _compositeReader is not null
                ? _compositeReader.HandleInnerTubeFailure(target, ex)
                : ClassifyStandaloneInnerTubeFailure(target.VideoId, ex);

            if (!outcome.LiveStreamEnded)
            {
                if (outcome.ResetCursorOnly)
                {
                    return await HandleInvalidPageTokenAsync(broadcast, cursor, channelLabel, ct).ConfigureAwait(false);
                }

                if (outcome.RequiresBotAuth)
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

                if (outcome.SwitchedToFallback)
                {
                    return TimeSpan.FromSeconds(1);
                }

                return HandleTransientFailure(ex, YouTubeChatIssue.TransientError, countTowardsGrpcFallback: false);
            }

            CoreLog.YouTubeInnerTubeChatEnded(logger, cursor.LiveChatId);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
            shouldRefreshLiveSnapshot = IsBroadcastCurrent(broadcast);
            resultDelay = MinimumStreamReconnectFloor;
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.InvalidPageToken)
        {
            return await HandleInvalidPageTokenAsync(broadcast, cursor, channelLabel, ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.LiveChatEnded)
        {
            logger.LogInformation(ex, "YouTube чат {LiveChatId} завершён, обновляем статус трансляции", cursor.LiveChatId);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
            shouldRefreshLiveSnapshot = IsBroadcastCurrent(broadcast);
            resultDelay = MinimumStreamReconnectFloor;
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
            return HandleTransientFailure(
                ex,
                YouTubeChatIssue.TransientError,
                countTowardsGrpcFallback: selectedTransport == YouTubeChatTransportKind.OfficialGrpc);
        }
        finally
        {
            ClearActiveStreamScope(streamScope.Value);
        }

        if (shouldRefreshLiveSnapshot && IsBroadcastCurrent(broadcast))
        {
            await streamMonitor.RefreshLiveSnapshotAsync(ct).ConfigureAwait(false);
        }

        return resultDelay;
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

    private async Task<IterationStepResult> ExecuteTransportStepAsync(
        YouTubeChatTarget target,
        YouTubeChatTransportKind selectedTransport,
        YouTubeBroadcastSummary broadcast,
        YouTubeChatSessionCursor cursor,
        string channelLabel,
        CancellationToken streamCt,
        CancellationToken loopCt)
    {
        var openedAt = timeProvider.GetUtcNow();
        var mappedTransport = MapTransport(selectedTransport);
        var defaultPolling = selectedTransport == YouTubeChatTransportKind.OfficialRest
            ? MinimumRestInterval
            : MinimumStreamReconnectFloor;
        var lastPollingInterval = defaultPolling;
        var sawChatEnded = false;
        var startedPublished = false;

        lock (_sync)
        {
            _state = ConnectionState.Connected;
            _transport = mappedTransport;
            _issue = YouTubeChatIssue.None;
            _connectedSince ??= openedAt;
        }

        var maxBatches = selectedTransport == YouTubeChatTransportKind.OfficialGrpc
            || (_compositeReader is null && chatReader.IsServerStream)
            ? (int?)null
            : 1;

        var candidateCursorToken = cursor.GetCursorTokenFor(selectedTransport, target.Mode);
        var batches = _compositeReader is not null
            ? _compositeReader.ReadSelectedTransportAsync(target, selectedTransport, candidateCursorToken, maxBatches, streamCt)
            : chatReader.ReadBatchesAsync(target, candidateCursorToken, maxBatches, streamCt);

        var transitionCommitted = false;
        var origin = GetOrigin(broadcast, cursor.LiveChatId);

        await foreach (var batch in batches.ConfigureAwait(false))
        {
            if (!origin.IsCurrent || !IsBroadcastCurrent(broadcast))
            {
                await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
                return new(TimeSpan.Zero, RefreshSnapshot: false);
            }

            if (!transitionCommitted)
            {
                transitionCommitted = true;
                await CommitTransportTransitionAsync(cursor, selectedTransport, target.Mode, channelLabel, loopCt)
                    .ConfigureAwait(false);
            }

            if (!startedPublished)
            {
                startedPublished = true;
                await MarkIngestionStartedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);
            }

            _compositeReader?.RecordSuccess(selectedTransport);
            lastPollingInterval = batch.PollingInterval;

            lock (_sync)
            {
                _consecutiveTransientFailures = 0;
                _reconnectAttempt = 0;
            }

            var batchOutcome = await ProcessBatchAsync(broadcast, cursor, batch, channelLabel, origin, loopCt).ConfigureAwait(false);

            if (batchOutcome.AbortedByBroadcastChange)
            {
                await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
                return new(TimeSpan.Zero, RefreshSnapshot: false);
            }

            if (batchOutcome.SawChatEnded)
            {
                sawChatEnded = true;
                break;
            }
        }

        if (!IsBroadcastCurrent(broadcast))
        {
            await ResetInactiveStateAsync(loopCt).ConfigureAwait(false);
            return new(TimeSpan.Zero, RefreshSnapshot: false);
        }

        if (sawChatEnded)
        {
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), loopCt).ConfigureAwait(false);
            return new(defaultPolling, RefreshSnapshot: IsBroadcastCurrent(broadcast));
        }

        if (selectedTransport == YouTubeChatTransportKind.OfficialGrpc)
        {
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
                return new(RapidCloseDelays[index], RefreshSnapshot: false);
            }

            var grpcInterval = lastPollingInterval > MinimumStreamReconnectFloor
                ? lastPollingInterval
                : MinimumStreamReconnectFloor;
            var remainingFloor = MinimumStreamReconnectFloor - elapsed;
            return new(remainingFloor > TimeSpan.Zero ? remainingFloor : grpcInterval, RefreshSnapshot: false);
        }

        lock (_sync)
        {
            _consecutiveTransientFailures = 0;
            _reconnectAttempt = 0;
        }

        if (selectedTransport == YouTubeChatTransportKind.OfficialRest)
        {
            return new(
                lastPollingInterval > MinimumRestInterval ? lastPollingInterval : MinimumRestInterval,
                RefreshSnapshot: false);
        }

        return new(
            lastPollingInterval > TimeSpan.Zero ? lastPollingInterval : MinimumStreamReconnectFloor,
            RefreshSnapshot: false);
    }

    private static CompositeYouTubeLiveChatReader CreateOfficialCompositeReader(
        IYouTubeLiveChatStreamClient streamClient,
        IYouTubeLiveChatPollingClient pollingClient,
        TimeProvider timeProvider) =>
        new(
            innerTubeReader: null,
            new OfficialGrpcYouTubeLiveChatReader(streamClient),
            new OfficialRestYouTubeLiveChatReader(pollingClient, timeProvider),
            timeProvider);

    private static bool IsOfficialTransport(YouTubeChatTransportKind transport) =>
        transport is YouTubeChatTransportKind.OfficialGrpc or YouTubeChatTransportKind.OfficialRest;

    private async Task CommitTransportTransitionAsync(
        YouTubeChatSessionCursor cursor,
        YouTubeChatTransportKind nextTransport,
        YouTubeChatMode nextMode,
        string channelLabel,
        CancellationToken ct)
    {
        var transitionNow = timeProvider.GetUtcNow();
        bool transportGapDetected;

        lock (_sync)
        {
            transportGapDetected = cursor.PrepareForTransport(nextTransport, nextMode, transitionNow);

            if (transportGapDetected)
            {
                _gapCount++;
            }
        }

        if (!transportGapDetected)
        {
            return;
        }

        CoreLog.YouTubeIncompatibleTransportSwitch(logger, cursor.LiveChatId, nextTransport.ToString());

        await eventBus.PublishAsync(
            new ChatReadingGapDetected(StreamingPlatform.YouTube, channelLabel, cursor.LiveChatId, transitionNow),
            ct).ConfigureAwait(false);
    }

    private InnerTubeFailureOutcome ClassifyStandaloneInnerTubeFailure(string? videoId, InnerTubeChatException exception)
    {
        if (exception.Kind == InnerTubeErrorKind.ContinuationInvalidated)
        {
            CoreLog.InnerTubeCursorReset(logger, videoId, exception.Message);
            return new(SwitchedToFallback: false, ResetCursorOnly: true, LiveStreamEnded: false, RequiresBotAuth: false, ShouldUseTransientBackoff: false);
        }

        return new(
            SwitchedToFallback: false,
            ResetCursorOnly: false,
            LiveStreamEnded: exception.Kind == InnerTubeErrorKind.LiveStreamEnded,
            RequiresBotAuth: false,
            ShouldUseTransientBackoff: exception.Kind != InnerTubeErrorKind.LiveStreamEnded);
    }

    private static string? GetBroadcastKey(YouTubeBroadcastSummary? broadcast)
    {
        if (broadcast is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(broadcast.LiveChatId))
        {
            return broadcast.LiveChatId;
        }

        return !string.IsNullOrWhiteSpace(broadcast.VideoId) ? broadcast.VideoId : null;
    }

    private static YouTubeChatTransport MapTransport(YouTubeChatTransportKind transport) =>
        transport switch
        {
            YouTubeChatTransportKind.InnerTube => YouTubeChatTransport.InnerTube,
            YouTubeChatTransportKind.OfficialGrpc => YouTubeChatTransport.GrpcStream,
            YouTubeChatTransportKind.OfficialRest => YouTubeChatTransport.RestPolling,
            _ => YouTubeChatTransport.None,
        };

    private async Task<BatchProcessOutcome> ProcessBatchAsync(
        YouTubeBroadcastSummary broadcast,
        YouTubeChatSessionCursor cursor,
        YouTubeLiveChatBatch batch,
        string channelLabel,
        ChatOrigin origin,
        CancellationToken ct)
    {
        if (!origin.IsCurrent || !IsBroadcastCurrent(broadcast))
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
        var broadcasterChannelId = origin.Channel.ChannelId;

        if (string.IsNullOrWhiteSpace(botChannelId))
        {
            botChannelId = accountsStore.Load(PlatformAuthRole.Bot).ChannelId;
        }

        var sawChatEnded = false;
        var historyScope = ChatHistoryScope.ForYouTube(broadcasterChannelId, cursor.LiveChatId, channelLabel);
        List<(ChatMessage Message, DateTimeOffset Timestamp)> historicalMessages = [];
        List<ChatModerationAction> historicalModerations = [];

        foreach (var item in batchResult.HistoricalItems)
        {
            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                sawChatEnded = true;
                continue;
            }

            var itemTimestampUtc = item.PublishedAtUtc ?? cursor.BoundaryUtc;

            if (item.Moderation is { } moderation)
            {
                if (TryMapModeration(moderation, itemTimestampUtc) is { } action)
                {
                    historicalModerations.Add(action);
                }

                if (moderation.ActionKind == YouTubeModerationActionKind.MessageReplaced
                    && moderation.ReplacementItem is { } replacement)
                {
                    var mappedReplacement = mapper.Map(replacement, channelLabel, botChannelId, broadcasterChannelId, cursor.LiveChatId);
                    if (mappedReplacement.Kind == YouTubeChatMapKind.ChatMessage && mappedReplacement.Message is not null)
                    {
                        historicalMessages.Add((mappedReplacement.Message, replacement.PublishedAtUtc ?? itemTimestampUtc));
                    }
                }

                cursor.MarkSeen(item.Id);
                continue;
            }

            var mapped = mapper.Map(item, channelLabel, botChannelId, broadcasterChannelId, cursor.LiveChatId);
            if (mapped.Kind == YouTubeChatMapKind.ChatMessage && mapped.Message is not null)
            {
                historicalMessages.Add((mapped.Message, itemTimestampUtc));
            }

            cursor.MarkSeen(item.Id);
        }

        if (origin.IsCurrent && IsBroadcastCurrent(broadcast))
        {
            if (historicalMessages.Count > 0)
            {
                await eventBus.PublishAsync(
                    new RawChatHistoryReceived(historyScope, historicalMessages, historicalModerations),
                    ct).ConfigureAwait(false);
            }
            else
            {
                foreach (var action in historicalModerations)
                {
                    await eventBus.PublishAsync(new RawChatModerationReceived(historyScope, action), ct).ConfigureAwait(false);
                }
            }
        }

        foreach (var item in batchResult.LiveItems)
        {
            if (!origin.IsCurrent || !IsBroadcastCurrent(broadcast))
            {
                DropHeldCandidatesForChat(cursor.LiveChatId);
                return new(sawChatEnded, AbortedByBroadcastChange: true);
            }

            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                sawChatEnded = true;
                continue;
            }

            var messageTimestampUtc = item.PublishedAtUtc ?? nowUtc;

            if (item.Moderation is { } liveModeration)
            {
                if (TryMapModeration(liveModeration, messageTimestampUtc) is { } action)
                {
                    await eventBus.PublishAsync(new RawChatModerationReceived(historyScope, action), ct).ConfigureAwait(false);
                }

                if (liveModeration.ActionKind == YouTubeModerationActionKind.MessageReplaced
                    && liveModeration.ReplacementItem is { } replacement)
                {
                    await PublishItemAsync(
                        replacement,
                        cursor.LiveChatId,
                        channelLabel,
                        botChannelId,
                        broadcasterChannelId,
                        replacement.PublishedAtUtc ?? messageTimestampUtc,
                        origin,
                        ct,
                        isReplacement: true).ConfigureAwait(false);
                }

                cursor.MarkSeen(item.Id);
                continue;
            }

            if (TryHoldSharedAccountCandidate(
                    cursor.LiveChatId,
                    item,
                    channelLabel,
                    botChannelId,
                    broadcasterChannelId,
                    messageTimestampUtc,
                    nowUtc,
                    origin,
                    ct,
                    out var overflowCandidate))
            {
                if (overflowCandidate is not null && IsBroadcastCurrent(overflowCandidate.LiveChatId))
                {
                    await PublishItemAsync(
                        overflowCandidate.Item,
                        overflowCandidate.LiveChatId,
                        overflowCandidate.ChannelLabel,
                        overflowCandidate.BotChannelId,
                        overflowCandidate.BroadcasterChannelId,
                        overflowCandidate.MessageTimestampUtc,
                        overflowCandidate.Origin,
                        ct).ConfigureAwait(false);
                }

                cursor.MarkSeen(item.Id);
                continue;
            }

            await PublishItemAsync(
                item,
                cursor.LiveChatId,
                channelLabel,
                botChannelId,
                broadcasterChannelId,
                messageTimestampUtc,
                origin,
                ct).ConfigureAwait(false);

            cursor.MarkSeen(item.Id);
        }

        if (!IsBroadcastCurrent(broadcast))
        {
            return new(sawChatEnded, AbortedByBroadcastChange: true);
        }

        cursor.CompleteBatch(batch, batchResult.LiveItems.Any(static item => item.Kind != YouTubeLiveChatItemKind.ChatEnded));
        return new(sawChatEnded, AbortedByBroadcastChange: false);
    }

    private async Task<TimeSpan> HandleInvalidPageTokenAsync(
        YouTubeBroadcastSummary broadcast,
        YouTubeChatSessionCursor cursor,
        string channelLabel,
        CancellationToken ct)
    {
        if (!IsBroadcastCurrent(broadcast))
        {
            await ResetInactiveStateAsync(ct).ConfigureAwait(false);
            return TimeSpan.Zero;
        }

        var nowUtc = timeProvider.GetUtcNow();

        lock (_sync)
        {
            _gapCount++;
        }

        cursor.ResetAfterInvalidToken(nowUtc);

        logger.LogWarning(
            "YouTube чат {LiveChatId}: курсор сообщений устарел, возможен пропуск старых сообщений при пересинхронизации",
            cursor.LiveChatId);

        await eventBus.PublishAsync(
            new ChatReadingGapDetected(StreamingPlatform.YouTube, channelLabel, cursor.LiveChatId, nowUtc),
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

        var boundedDelay = remaining > QuotaWaitCheckCap ? QuotaWaitCheckCap : remaining;
        var innerTubeCooldown = _compositeReader?.GetRemainingInnerTubeCooldown();

        if (innerTubeCooldown is { } cooldown && cooldown < boundedDelay)
        {
            return cooldown;
        }

        return boundedDelay;
    }

    private Task MarkIngestionStartedAsync(DateTimeOffset at, CancellationToken ct)
    {
        string? channelId;
        lock (_sync)
        {
            if (_ingestionPublished)
            {
                return Task.CompletedTask;
            }

            _ingestionPublished = true;
            channelId = _origin?.Channel.ChannelId;
        }

        return eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.YouTube, at, channelId), ct);
    }

    private Task MarkIngestionStoppedAsync(DateTimeOffset at, CancellationToken ct)
    {
        TaskCompletionSource? ownedTcs = null;
        Task? inFlightStop = null;
        long generation = 0;
        string? channelId = null;

        lock (_sync)
        {
            channelId = _origin?.Channel.ChannelId;
            CloseOrigin();
            if (!_ingestionPublished)
            {
                inFlightStop = _pendingIngestionStopTask;
            }
            else
            {
                _ingestionPublished = false;
                generation = ++_ingestionStopGeneration;
                ownedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingIngestionStopTask = ownedTcs.Task;
            }
        }

        if (ownedTcs is not null)
        {
            return PublishIngestionStoppedCoreAsync(at, generation, channelId, ownedTcs);
        }

        return inFlightStop is not null ? inFlightStop.WaitAsync(ct) : Task.CompletedTask;
    }

    private async Task PublishIngestionStoppedCoreAsync(
        DateTimeOffset at,
        long generation,
        string? channelId,
        TaskCompletionSource stopTcs)
    {
        try
        {
            await eventBus.PublishAsync(new ChatIngestionStopped(StreamingPlatform.YouTube, at, channelId), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (_ingestionStopGeneration == generation)
                {
                    _pendingIngestionStopTask = null;
                }
            }

            stopTcs.TrySetResult();
        }
    }

    private void SwitchToRestFallback(string reason)
    {
        _compositeReader?.SwitchToRestFallback();
        logger.LogWarning("YouTube чат: переключение на REST polling ({Reason}); повторная проба gRPC через 10 мин", reason);
    }

    private TimeSpan HandleTransientFailure(Exception exception, YouTubeChatIssue issue, bool countTowardsGrpcFallback)
    {
        var switchedToRest = countTowardsGrpcFallback
            && (_compositeReader?.RecordGrpcTransientFailure() ?? false);
        int failures;

        lock (_sync)
        {
            _consecutiveTransientFailures++;
            _reconnectAttempt++;
            failures = _consecutiveTransientFailures;
            _state = ConnectionState.Reconnecting;
            _issue = issue;
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

    private async Task OnActiveBroadcastChangedAsync(YouTubeActiveBroadcastChanged @event, CancellationToken cancellationToken)
    {
        CancellationTokenSource? streamCts = null;
        Task? activeReadTask = null;
        var shouldStopIngestion = false;

        lock (_sync)
        {
            if (!BroadcastMatches(_originBroadcast, @event.ActiveBroadcast))
            {
                CloseOrigin();
            }

            var activeChanged = !BroadcastMatches(_activeStreamBroadcast, @event.ActiveBroadcast);

            if (_activeStreamCts is not null && activeChanged)
            {
                streamCts = _activeStreamCts;
            }

            if (activeChanged)
            {
                activeReadTask = _activeReadTcs?.Task;
            }

            var activeKey = GetBroadcastKey(@event.ActiveBroadcast);
            shouldStopIngestion = activeKey is null
                || (_cursor is not null
                    && !string.Equals(_cursor.LiveChatId, activeKey, StringComparison.Ordinal));
        }

        if (streamCts is not null)
        {
            try
            {
                await streamCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (activeReadTask is not null)
        {
            await activeReadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (shouldStopIngestion)
        {
            await ClearAndAwaitHoldDrainAsync().ConfigureAwait(false);
            await MarkIngestionStoppedAsync(timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        Wake();
    }

    private ActiveStreamScope? CreateActiveStreamScope(YouTubeBroadcastSummary broadcast, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var readTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeStreamCts = cts;
            _activeStreamBroadcast = broadcast;
            _activeReadTcs = readTcs;
            return new(cts, readTcs);
        }
    }

    private void ClearActiveStreamScope(ActiveStreamScope scope)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeStreamCts, scope.Cts))
            {
                _activeStreamCts = null;
                _activeStreamBroadcast = null;
            }

            if (ReferenceEquals(_activeReadTcs, scope.ReadTcs))
            {
                _activeReadTcs = null;
            }
        }

        scope.ReadTcs.TrySetResult();
    }

    private bool IsBroadcastCurrent(YouTubeBroadcastSummary broadcast)
    {
        return BroadcastMatches(streamMonitor.CurrentBroadcast, broadcast);
    }

    private ChatOrigin GetOrigin(YouTubeBroadcastSummary broadcast, string chatId)
    {
        lock (_sync)
        {
            if (_origin is null || !BroadcastMatches(_originBroadcast, broadcast))
            {
                CloseOrigin();
                _origin = new(new(StreamingPlatform.YouTube, broadcast.ChannelId), chatId);
                _originBroadcast = broadcast;
            }

            return _origin;
        }
    }

    private void CloseOrigin()
    {
        _origin?.Close();
        _origin = null;
        _originBroadcast = null;
    }

    private bool IsBroadcastCurrent(string liveChatId)
    {
        return string.Equals(GetBroadcastKey(streamMonitor.CurrentBroadcast), liveChatId, StringComparison.Ordinal);
    }

    private static bool BroadcastMatches(YouTubeBroadcastSummary? current, YouTubeBroadcastSummary? candidate)
    {
        return current is not null
            && candidate is not null
            && string.Equals(current.VideoId, candidate.VideoId, StringComparison.Ordinal)
            && string.Equals(current.ChannelId, candidate.ChannelId, StringComparison.Ordinal)
            && string.Equals(current.LiveChatId, candidate.LiveChatId, StringComparison.Ordinal);
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
        ChatOrigin origin,
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

        if (!isSharedAccount || _sentMessageTracker.Contains(item.Id, liveChatId))
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
                nowUtc + MaxEchoHoldDuration,
                origin));

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

                if (head.WatermarkSeq > 0 && !_sentMessageTracker.Contains(head.Item.Id, head.LiveChatId))
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
                        released.LiveChatId,
                        released.ChannelLabel,
                        released.BotChannelId,
                        released.BroadcasterChannelId,
                        released.MessageTimestampUtc,
                        released.Origin,
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
        string? liveChatId,
        string channelLabel,
        string? botChannelId,
        string? broadcasterChannelId,
        DateTimeOffset timestamp,
        ChatOrigin origin,
        CancellationToken ct,
        bool isReplacement = false)
    {
        if (!origin.IsCurrent)
        {
            return;
        }

        var mapped = mapper.Map(item, channelLabel, botChannelId, broadcasterChannelId, liveChatId);

        if (mapped.Kind == YouTubeChatMapKind.ChatMessage && mapped.Message is not null)
        {
            await eventBus.PublishAsync(new RawChatMessageReceived(mapped.Message with { Origin = origin }, timestamp, isReplacement), ct).ConfigureAwait(false);

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

    private static ChatModerationAction? TryMapModeration(YouTubeModerationDetails moderation, DateTimeOffset cutoffUtc)
    {
        return moderation.ActionKind switch
        {
            YouTubeModerationActionKind.MessageDeleted => new(
                ChatModerationActionKind.MessageDeleted,
                moderation.TargetMessageId,
                null,
                cutoffUtc),
            YouTubeModerationActionKind.MessageReplaced => new(
                ChatModerationActionKind.MessageReplaced,
                moderation.TargetMessageId,
                null,
                cutoffUtc,
                moderation.ReplacementItem?.Id),
            YouTubeModerationActionKind.AuthorMessagesDeleted
                or YouTubeModerationActionKind.UserBannedTemporary
                or YouTubeModerationActionKind.UserBannedPermanent => new(
                ChatModerationActionKind.AuthorMessagesDeleted,
                null,
                moderation.TargetChannelId,
                cutoffUtc),
            _ => null,
        };
    }

    private sealed record HeldEchoCandidate(
        string LiveChatId,
        YouTubeLiveChatItem Item,
        string ChannelLabel,
        string? BotChannelId,
        string? BroadcasterChannelId,
        long WatermarkSeq,
        DateTimeOffset MessageTimestampUtc,
        DateTimeOffset ExpiresAtUtc,
        ChatOrigin Origin);

    private readonly record struct BatchProcessOutcome(bool SawChatEnded, bool AbortedByBroadcastChange);

    private readonly record struct IterationStepResult(TimeSpan Delay, bool RefreshSnapshot);

    private readonly record struct ActiveStreamScope(CancellationTokenSource Cts, TaskCompletionSource ReadTcs);
}
