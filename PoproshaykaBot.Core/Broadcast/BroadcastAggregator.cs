using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class BroadcastAggregator :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventSubscriber,
    IBroadcastState,
    IBroadcastChannelLifecycle,
    IBroadcastOfflineStopCoordinator,
    IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IBroadcastDraftCoordinator _draftCoordinator;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BroadcastAggregator> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly Lock _syncLock = new();
    private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);

    private readonly Dictionary<StreamingPlatform, LivePlatformEntry> _livePlatforms = [];
    private readonly Dictionary<StreamingPlatform, PendingOfflineEntry> _pendingOffline = [];
    private readonly Dictionary<StreamingPlatform, Func<CancellationToken, Task>> _pendingStops = [];

    private Guid? _currentBroadcastId;
    private bool _offlineContinuationScheduled;
    private bool _disposed;

    public BroadcastAggregator(
        IEventBus eventBus,
        IBroadcastDraftCoordinator draftCoordinator,
        ITargetChannelProvider targetChannelProvider,
        TimeProvider timeProvider,
        ILogger<BroadcastAggregator> logger)
    {
        _eventBus = eventBus;
        _draftCoordinator = draftCoordinator;
        _targetChannelProvider = targetChannelProvider;
        _timeProvider = timeProvider;
        _logger = logger;

        _onlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
    }

    public bool IsAnyLive
    {
        get
        {
            lock (_syncLock)
            {
                return _livePlatforms.Count > 0;
            }
        }
    }

    public Guid? CurrentBroadcastId
    {
        get
        {
            lock (_syncLock)
            {
                return _currentBroadcastId;
            }
        }
    }

    public IReadOnlySet<StreamingPlatform> LivePlatforms
    {
        get
        {
            lock (_syncLock)
            {
                return _livePlatforms.Keys.ToHashSet();
            }
        }
    }

    public bool IsPlatformLive(StreamingPlatform platform)
    {
        lock (_syncLock)
        {
            return _livePlatforms.ContainsKey(platform);
        }
    }

    public void ScheduleOfflineStop(StreamingPlatform platform, Func<CancellationToken, Task> stopAction)
    {
        ArgumentNullException.ThrowIfNull(stopAction);

        bool scheduleContinuation;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _pendingStops[platform] = stopAction;
            scheduleContinuation = !_offlineContinuationScheduled;
            _offlineContinuationScheduled = true;
        }

        if (scheduleContinuation)
        {
            _eventBus.ContinueAfterPublish(ProcessOfflineTransitionsAfterPublishAsync);
        }
    }

    public async Task LeaveActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey? expectedChannel,
        CancellationToken cancellationToken = default)
    {
        await _transitionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            BroadcastEnded? endedEvent = null;
            BroadcastPlatformLeft? leftEvent = null;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                if (_pendingOffline.TryGetValue(platform, out var pending)
                    && (expectedChannel is null || MatchesOfflineChannel(new LivePlatformEntry(pending.Channel, pending.ChannelId, null, now), expectedChannel.Value.ChannelId, null)))
                {
                    _pendingOffline.Remove(platform);
                }

                _pendingStops.Remove(platform);

                if (_livePlatforms.TryGetValue(platform, out var liveA)
                    && (expectedChannel is null || MatchesOfflineChannel(liveA, expectedChannel.Value.ChannelId, null)))
                {
                    _livePlatforms.Remove(platform);
                    if (_currentBroadcastId is { } broadcastId)
                    {
                        if (_livePlatforms.Count == 0)
                        {
                            _currentBroadcastId = null;
                            endedEvent = new BroadcastEnded(broadcastId, platform, liveA.Channel, now, IsCatchUp: false, liveA.ChannelId);
                        }
                        else
                        {
                            leftEvent = new BroadcastPlatformLeft(broadcastId, platform, liveA.Channel, now, IsCatchUp: false, liveA.ChannelId);
                        }
                    }
                }
            }

            if (endedEvent is not null)
            {
                await _eventBus.PublishAsync(endedEvent, cancellationToken).ConfigureAwait(false);
            }
            else if (leftEvent is not null)
            {
                await _eventBus.PublishAsync(leftEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionSemaphore.Release();
        }
    }

    public async Task ReplaceActiveChannelAsync(
        StreamingPlatform platform,
        ChannelKey? fromChannel,
        ChannelCatalogEntry toChannel,
        StreamInfo? confirmedStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toChannel);

        await _transitionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var switchAt = _timeProvider.GetUtcNow();
            var labelB = !string.IsNullOrWhiteSpace(toChannel.DisplayName)
                ? toChannel.DisplayName
                : (!string.IsNullOrWhiteSpace(toChannel.Handle) ? toChannel.Handle : toChannel.ChannelId);
            var streamStart = ResolveStartedAt(confirmedStream, switchAt);
            var startedAt = streamStart > switchAt ? switchAt : streamStart;

            BroadcastPlatformLeft? leftEvent = null;
            BroadcastPlatformJoined? joinedEvent = null;
            BroadcastStarted? startedEvent = null;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                _pendingOffline.Remove(platform);
                _pendingStops.Remove(platform);

                if (_currentBroadcastId is { } broadcastId)
                {
                    if (_livePlatforms.TryGetValue(platform, out var liveA))
                    {
                        if (fromChannel is null || MatchesOfflineChannel(liveA, fromChannel.Value.ChannelId, null))
                        {
                            _livePlatforms[platform] = new LivePlatformEntry(labelB, toChannel.ChannelId, confirmedStream, switchAt);
                            leftEvent = new BroadcastPlatformLeft(broadcastId, platform, liveA.Channel, switchAt, IsCatchUp: false, liveA.ChannelId);
                            joinedEvent = new BroadcastPlatformJoined(broadcastId, platform, labelB, confirmedStream, switchAt, IsCatchUp: false, toChannel.ChannelId);
                        }
                        else
                        {
                            _livePlatforms[platform] = new LivePlatformEntry(labelB, toChannel.ChannelId, confirmedStream, switchAt);
                            joinedEvent = new BroadcastPlatformJoined(broadcastId, platform, labelB, confirmedStream, switchAt, IsCatchUp: false, toChannel.ChannelId);
                        }
                    }
                    else
                    {
                        _livePlatforms[platform] = new LivePlatformEntry(labelB, toChannel.ChannelId, confirmedStream, switchAt);
                        joinedEvent = new BroadcastPlatformJoined(broadcastId, platform, labelB, confirmedStream, switchAt, IsCatchUp: false, toChannel.ChannelId);
                    }
                }
                else
                {
                    var newBroadcastId = _draftCoordinator.ResolveBroadcastIdOnStart(platform, labelB, confirmedStream, startedAt);
                    _currentBroadcastId = newBroadcastId;
                    _livePlatforms[platform] = new LivePlatformEntry(labelB, toChannel.ChannelId, confirmedStream, startedAt);
                    startedEvent = new BroadcastStarted(newBroadcastId, platform, labelB, confirmedStream, startedAt, IsCatchUp: false, toChannel.ChannelId);
                }
            }

            if (leftEvent is not null)
            {
                await _eventBus.PublishAsync(leftEvent, cancellationToken).ConfigureAwait(false);
            }

            if (joinedEvent is not null)
            {
                await _eventBus.PublishAsync(joinedEvent, cancellationToken).ConfigureAwait(false);
            }

            if (startedEvent is not null)
            {
                await _eventBus.PublishAsync(startedEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionSemaphore.Release();
        }
    }

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        await _transitionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var startedAt = ResolveStartedAt(@event.Stream, now);
            var channel = string.IsNullOrEmpty(@event.Channel)
                ? _targetChannelProvider.Current.Login
                : @event.Channel;
            var channelId = ResolveChannelId(@event.ChannelId, @event.Stream);

            BroadcastEnded? replacedBroadcast = null;
            BroadcastPlatformLeft? replacedPlatform = null;
            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                if (_currentBroadcastId is { } previousBroadcastId
                    && _livePlatforms.TryGetValue(@event.Platform, out var previous)
                    && (IsDifferentChannel(previous.ChannelId, previous.Channel, channelId, channel)
                        || (previous.Stream?.Id is { Length: > 0 } previousStreamId
                            && @event.Stream?.Id is { Length: > 0 } nextStreamId
                            && !string.Equals(previousStreamId, nextStreamId, StringComparison.Ordinal))))
                {
                    var closedAt = startedAt < now ? startedAt : now;
                    if (closedAt < previous.StartedAt)
                    {
                        closedAt = previous.StartedAt;
                    }

                    _livePlatforms.Remove(@event.Platform);
                    if (_livePlatforms.Count == 0)
                    {
                        _currentBroadcastId = null;
                        replacedBroadcast = new(previousBroadcastId, @event.Platform, previous.Channel, closedAt, @event.IsCatchUp, previous.ChannelId);
                    }
                    else
                    {
                        replacedPlatform = new(previousBroadcastId, @event.Platform, previous.Channel, closedAt, @event.IsCatchUp, previous.ChannelId);
                    }
                }
            }

            if (replacedBroadcast is not null)
            {
                await _eventBus.PublishAsync(replacedBroadcast, cancellationToken).ConfigureAwait(false);
            }
            else if (replacedPlatform is not null)
            {
                await _eventBus.PublishAsync(replacedPlatform, cancellationToken).ConfigureAwait(false);
            }

            BroadcastStarted? startedEvent = null;
            BroadcastPlatformJoined? joinedEvent = null;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                _pendingOffline.Remove(@event.Platform);
                _pendingStops.Remove(@event.Platform);

                if (_livePlatforms.ContainsKey(@event.Platform))
                {
                    _livePlatforms[@event.Platform] = new LivePlatformEntry(channel, channelId, @event.Stream, startedAt);
                    return;
                }

                _livePlatforms[@event.Platform] = new LivePlatformEntry(channel, channelId, @event.Stream, startedAt);

                if (_currentBroadcastId is null)
                {
                    var broadcastId = _draftCoordinator.ResolveBroadcastIdOnStart(@event.Platform, channel, @event.Stream, startedAt);
                    _currentBroadcastId = broadcastId;
                    startedEvent = new BroadcastStarted(
                        broadcastId,
                        @event.Platform,
                        channel,
                        @event.Stream,
                        startedAt,
                        @event.IsCatchUp,
                        channelId);
                }
                else
                {
                    joinedEvent = new BroadcastPlatformJoined(
                        _currentBroadcastId.Value,
                        @event.Platform,
                        channel,
                        @event.Stream,
                        now,
                        @event.IsCatchUp,
                        channelId);
                }
            }

            if (startedEvent is not null)
            {
                _logger.LogInformation(
                    "BroadcastAggregator: начат эфир {BroadcastId} (площадка {Platform}, канал {Channel})",
                    startedEvent.BroadcastId,
                    startedEvent.FirstPlatform,
                    startedEvent.Channel);

                await _eventBus.PublishAsync(startedEvent, cancellationToken).ConfigureAwait(false);
            }
            else if (joinedEvent is not null)
            {
                _logger.LogInformation(
                    "BroadcastAggregator: к эфиру {BroadcastId} подключилась площадка {Platform} ({Channel})",
                    joinedEvent.BroadcastId,
                    joinedEvent.Platform,
                    joinedEvent.Channel);

                await _eventBus.PublishAsync(joinedEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionSemaphore.Release();
        }
    }

    public Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var explicitChannel = string.IsNullOrWhiteSpace(@event.Channel) ? null : @event.Channel.Trim();
        var explicitChannelId = string.IsNullOrWhiteSpace(@event.ChannelId) ? null : @event.ChannelId.Trim();
        var channel = explicitChannel ?? _targetChannelProvider.Current.Login;

        bool scheduleContinuation;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_livePlatforms.TryGetValue(@event.Platform, out var live))
            {
                if (!MatchesOfflineChannel(live, explicitChannelId, explicitChannel))
                {
                    return Task.CompletedTask;
                }

                _pendingOffline[@event.Platform] = new PendingOfflineEntry(
                    channel,
                    explicitChannelId ?? live.ChannelId,
                    live.Stream?.Id,
                    now,
                    @event.IsCatchUp);
            }

            if (@event.IsCatchUp)
            {
                _pendingStops.Remove(@event.Platform);
            }

            scheduleContinuation = !_offlineContinuationScheduled;
            _offlineContinuationScheduled = true;
        }

        if (scheduleContinuation)
        {
            _eventBus.ContinueAfterPublish(ProcessOfflineTransitionsAfterPublishAsync);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingOffline.Clear();
            _pendingStops.Clear();
            _livePlatforms.Clear();
            _currentBroadcastId = null;
        }

        _onlineSubscription.Dispose();
        _offlineSubscription.Dispose();
    }

    private async Task ProcessOfflineTransitionsAfterPublishAsync()
    {
        List<BroadcastPlatformLeft> leftEvents = [];
        BroadcastEnded? endedEvent = null;
        List<(StreamingPlatform Platform, Func<CancellationToken, Task> StopAction)> stopsToRun = [];

        await _transitionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_syncLock)
            {
                _offlineContinuationScheduled = false;

                if (_disposed)
                {
                    return;
                }

                foreach (var (platform, pending) in _pendingOffline.ToArray())
                {
                    _pendingOffline.Remove(platform);

                    if (!_livePlatforms.TryGetValue(platform, out var currentLive)
                        || !MatchesOfflineChannel(currentLive, pending.ChannelId, pending.Channel)
                        || (!string.IsNullOrEmpty(currentLive.Stream?.Id)
                            && !string.IsNullOrEmpty(pending.StreamId)
                            && !string.Equals(currentLive.Stream.Id, pending.StreamId, StringComparison.Ordinal))
                        || !_livePlatforms.Remove(platform))
                    {
                        continue;
                    }

                    if (_currentBroadcastId is not { } broadcastId)
                    {
                        continue;
                    }

                    if (_livePlatforms.Count == 0)
                    {
                        _currentBroadcastId = null;
                        endedEvent = new BroadcastEnded(broadcastId, platform, pending.Channel, pending.OfflineAt, pending.IsCatchUp, pending.ChannelId);
                    }
                    else
                    {
                        leftEvents.Add(new BroadcastPlatformLeft(broadcastId, platform, pending.Channel, pending.OfflineAt, pending.IsCatchUp, pending.ChannelId));
                    }
                }

                foreach (var (platform, stopAction) in _pendingStops.ToArray())
                {
                    _pendingStops.Remove(platform);

                    if (!_livePlatforms.ContainsKey(platform))
                    {
                        stopsToRun.Add((platform, stopAction));
                    }
                }
            }

            foreach (var left in leftEvents)
            {
                _logger.LogInformation(
                    "BroadcastAggregator: площадка {Platform} вышла из эфира {BroadcastId}",
                    left.Platform,
                    left.BroadcastId);

                await _eventBus.PublishAsync(left, CancellationToken.None).ConfigureAwait(false);
            }

            if (endedEvent is not null)
            {
                _logger.LogInformation(
                    "BroadcastAggregator: завершён эфир {BroadcastId} (последняя площадка {Platform}, канал {Channel})",
                    endedEvent.BroadcastId,
                    endedEvent.LastPlatform,
                    endedEvent.LastChannel);

                await _eventBus.PublishAsync(endedEvent, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionSemaphore.Release();
        }

        foreach (var (platform, stopAction) in stopsToRun)
        {
            if (IsPlatformLive(platform))
            {
                _logger.LogInformation("BroadcastAggregator: авто-отключение {Platform} отменено – площадка снова в сети", platform);
                continue;
            }

            try
            {
                await stopAction(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "BroadcastAggregator: ошибка авто-отключения площадки {Platform}", platform);
            }
        }
    }

    private static string? ResolveChannelId(string? eventChannelId, StreamInfo? stream)
    {
        if (!string.IsNullOrWhiteSpace(eventChannelId))
        {
            return eventChannelId.Trim();
        }

        return string.IsNullOrWhiteSpace(stream?.UserId) ? null : stream.UserId.Trim();
    }

    private static bool MatchesOfflineChannel(LivePlatformEntry live, string? channelId, string? channel)
    {
        if (!string.IsNullOrWhiteSpace(live.ChannelId) && !string.IsNullOrWhiteSpace(channelId))
        {
            return string.Equals(live.ChannelId, channelId, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(channel)
            || string.Equals(live.Channel, channel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDifferentChannel(string? leftId, string leftChannel, string? rightId, string rightChannel)
    {
        if (!string.IsNullOrWhiteSpace(leftId) && !string.IsNullOrWhiteSpace(rightId))
        {
            return !string.Equals(leftId, rightId, StringComparison.Ordinal);
        }

        return !string.Equals(leftChannel, rightChannel, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset ResolveStartedAt(StreamInfo? stream, DateTimeOffset fallbackNow)
    {
        return stream?.StartedAt is { } streamStart && streamStart != default
            ? new(DateTime.SpecifyKind(streamStart, DateTimeKind.Utc))
            : fallbackNow;
    }

    private sealed record LivePlatformEntry(string Channel, string? ChannelId, StreamInfo? Stream, DateTimeOffset StartedAt);

    private sealed record PendingOfflineEntry(string Channel, string? ChannelId, string? StreamId, DateTimeOffset OfflineAt, bool IsCatchUp);
}
