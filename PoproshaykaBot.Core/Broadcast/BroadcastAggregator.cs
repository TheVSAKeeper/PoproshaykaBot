using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class BroadcastAggregator :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventSubscriber,
    IBroadcastState,
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

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var startedAt = ResolveStartedAt(@event.Stream, now);
        var channel = string.IsNullOrEmpty(@event.Channel)
            ? _targetChannelProvider.Current.Login
            : @event.Channel;

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
                _livePlatforms[@event.Platform] = new LivePlatformEntry(channel, @event.Stream, startedAt);
                return;
            }

            _livePlatforms[@event.Platform] = new LivePlatformEntry(channel, @event.Stream, startedAt);

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
                    @event.IsCatchUp);
            }
            else
            {
                joinedEvent = new BroadcastPlatformJoined(
                    _currentBroadcastId.Value,
                    @event.Platform,
                    channel,
                    @event.Stream,
                    now,
                    @event.IsCatchUp);
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

    public Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        if (@event.IsCatchUp)
        {
            return Task.CompletedTask;
        }

        var now = _timeProvider.GetUtcNow();
        var channel = string.IsNullOrEmpty(@event.Channel)
            ? _targetChannelProvider.Current.Login
            : @event.Channel;

        bool scheduleContinuation;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_livePlatforms.ContainsKey(@event.Platform))
            {
                _pendingOffline[@event.Platform] = new PendingOfflineEntry(channel, now);
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

                if (!_livePlatforms.Remove(platform))
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
                    endedEvent = new BroadcastEnded(broadcastId, platform, pending.Channel, pending.OfflineAt);
                }
                else
                {
                    leftEvents.Add(new BroadcastPlatformLeft(broadcastId, platform, pending.Channel, pending.OfflineAt));
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

    private static DateTimeOffset ResolveStartedAt(StreamInfo? stream, DateTimeOffset fallbackNow)
    {
        return stream?.StartedAt is { } streamStart && streamStart != default
            ? new(DateTime.SpecifyKind(streamStart, DateTimeKind.Utc))
            : fallbackNow;
    }

    private sealed record LivePlatformEntry(string Channel, StreamInfo? Stream, DateTimeOffset StartedAt);

    private sealed record PendingOfflineEntry(string Channel, DateTimeOffset OfflineAt);
}
