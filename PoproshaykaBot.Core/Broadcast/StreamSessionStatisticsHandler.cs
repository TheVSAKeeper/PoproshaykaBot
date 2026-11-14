using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class StreamSessionStatisticsHandler :
    IEventHandler<BroadcastStarted>,
    IEventHandler<BroadcastPlatformJoined>,
    IEventHandler<BroadcastPlatformLeft>,
    IEventHandler<BroadcastEnded>,
    IEventHandler<StreamWentOffline>,
    IEventHandler<StreamMetadataResolved>,
    IEventHandler<ViewerSampleObserved>,
    IEventHandler<ChatMessageReceived>,
    IEventHandler<ChannelUpdated>,
    IEventHandler<ChatIngestionStarted>,
    IEventHandler<ChatIngestionStopped>,
    IEventSubscriber,
    IAsyncDisposable
{
    private const string MissingValuePlaceholder = "–";
    private static readonly TimeSpan ViewerSampleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InitialSeedDedupWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FutureSampleTolerance = TimeSpan.FromSeconds(5);

    internal static readonly TimeSpan StreamMatchTolerance = TimeSpan.FromMinutes(5);

    private readonly IBroadcastMessenger _messenger;
    private readonly IBroadcastState _broadcastState;
    private readonly IBroadcastDraftCoordinator _draftCoordinator;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly SettingsManager _settingsManager;
    private readonly TimeProvider _timeProvider;
    private readonly IEventBus _eventBus;
    private readonly ActiveBroadcastStore _activeBroadcastStore;
    private readonly ILogger<StreamSessionStatisticsHandler> _logger;
    private readonly IDisposable[] _subscriptions;

    private readonly Lock _stateLock = new();
    private readonly Dictionary<StreamingPlatform, PlatformCollector> _platforms = [];
    private readonly Dictionary<StreamingPlatform, DateTimeOffset> _ingestionSinceByPlatform = [];
    private readonly List<BroadcastViewerSample> _viewerSeries = [];

    private Guid? _activeBroadcastId;
    private DateTimeOffset? _broadcastStartedAt;
    private DateTimeOffset? _nextSampleTickUtc;

    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;
    private bool _disposed;

    public StreamSessionStatisticsHandler(
        IBroadcastMessenger messenger,
        IBroadcastState broadcastState,
        IBroadcastDraftCoordinator draftCoordinator,
        ITargetChannelProvider targetChannelProvider,
        SettingsManager settingsManager,
        TimeProvider timeProvider,
        IEventBus eventBus,
        ActiveBroadcastStore activeBroadcastStore,
        ILogger<StreamSessionStatisticsHandler> logger)
    {
        _messenger = messenger;
        _broadcastState = broadcastState;
        _draftCoordinator = draftCoordinator;
        _targetChannelProvider = targetChannelProvider;
        _settingsManager = settingsManager;
        _timeProvider = timeProvider;
        _eventBus = eventBus;
        _activeBroadcastStore = activeBroadcastStore;
        _logger = logger;

        _subscriptions =
        [
            eventBus.Subscribe<BroadcastStarted>(this),
            eventBus.Subscribe<BroadcastPlatformJoined>(this),
            eventBus.Subscribe<BroadcastPlatformLeft>(this),
            eventBus.Subscribe<BroadcastEnded>(this),
            eventBus.Subscribe<StreamWentOffline>(this),
            eventBus.Subscribe<StreamMetadataResolved>(this),
            eventBus.Subscribe<ViewerSampleObserved>(this),
            eventBus.Subscribe<ChatMessageReceived>(this),
            eventBus.Subscribe<ChannelUpdated>(this),
            eventBus.Subscribe<ChatIngestionStarted>(this),
            eventBus.Subscribe<ChatIngestionStopped>(this),
        ];
    }

    public async Task HandleAsync(BroadcastStarted @event, CancellationToken cancellationToken)
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            _logger.LogInformation("Статистика сессии не собирается – идёт отладка на чужом канале в общем профиле данных");
            return;
        }

        await StopSamplingLoopAsync().ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var startedAt = ResolveStartedAt(@event.Stream, @event.StartedAt);
        var channel = string.IsNullOrEmpty(@event.Channel)
            ? _targetChannelProvider.Current.Login
            : @event.Channel;
        var streamId = NullIfEmpty(@event.Stream?.Id);
        var initialViewers = @event.Stream?.ViewerCount ?? 0;
        var initialTitle = NullIfEmpty(@event.Stream?.Title);
        var initialGame = NullIfEmpty(@event.Stream?.GameName);

        _draftCoordinator.RecoverCompletedDraftOnStartup();

        var existingDraft = _activeBroadcastStore.Load();
        if (existingDraft is { EndedAt: null } && existingDraft.BroadcastId != @event.BroadcastId)
        {
            var staleRecord = _draftCoordinator.FinalizeCompletedDraft(existingDraft);
            await _eventBus.PublishAsync(new StreamSessionCompleted(staleRecord), cancellationToken).ConfigureAwait(false);
            existingDraft = null;
        }

        var resume = @event.IsCatchUp
                     && existingDraft is { EndedAt: null }
                     && existingDraft.BroadcastId == @event.BroadcastId
                     && existingDraft.Platforms.Exists(p => p.StartedAt != default && !string.IsNullOrEmpty(p.Channel));

        lock (_stateLock)
        {
            _platforms.Clear();
            _viewerSeries.Clear();
            _activeBroadcastId = @event.BroadcastId;

            if (resume && existingDraft is not null)
            {
                _broadcastStartedAt = existingDraft.StartedAt != default ? existingDraft.StartedAt : startedAt;
                _viewerSeries.AddRange(existingDraft.ViewerSeries);
                _nextSampleTickUtc = AdvanceTickCursorAfter(existingDraft.NextSampleTickUtc, now);

                foreach (var platformDraft in existingDraft.Platforms)
                {
                    if (platformDraft.StartedAt == default || string.IsNullOrEmpty(platformDraft.Channel))
                    {
                        continue;
                    }

                    var collector = RestorePlatformCollector(platformDraft);
                    _platforms[platformDraft.Platform] = collector;

                    if (platformDraft.Platform == @event.FirstPlatform)
                    {
                        collector.StreamId = streamId ?? collector.StreamId;
                        ApplyMetadata(collector, initialTitle, initialGame, now);

                        var current = collector.Segments[^1];
                        if (initialViewers > current.PeakViewers)
                        {
                            current.PeakViewers = initialViewers;
                        }
                    }

                    if (collector.EndedAt is null && _ingestionSinceByPlatform.TryGetValue(collector.Platform, out var ingestionSince))
                    {
                        OpenTracked(collector, ingestionSince);
                    }
                }

                if (!_platforms.ContainsKey(@event.FirstPlatform))
                {
                    AddFreshPlatformCollector(@event.FirstPlatform, channel, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
                }
            }
            else
            {
                _broadcastStartedAt = startedAt;
                _nextSampleTickUtc = now + ViewerSampleInterval;
                AddFreshPlatformCollector(@event.FirstPlatform, channel, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
            }
        }

        Checkpoint();
        StartSamplingLoop();

        if (resume)
        {
            _logger.LogInformation(
                "Сбор статистики эфира {BroadcastId} возобновлён из черновика",
                @event.BroadcastId);
        }
        else
        {
            _logger.LogDebug(
                "Сбор статистики эфира {BroadcastId} запущен (площадка {Platform}, старт {StartedAt}, catch-up {IsCatchUp}, зрители {Viewers})",
                @event.BroadcastId,
                @event.FirstPlatform,
                startedAt,
                @event.IsCatchUp,
                initialViewers);
        }
    }

    public Task HandleAsync(BroadcastPlatformJoined @event, CancellationToken cancellationToken)
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            return Task.CompletedTask;
        }

        var now = _timeProvider.GetUtcNow();
        var startedAt = ResolveStartedAt(@event.Stream, @event.JoinedAt);
        var channel = string.IsNullOrEmpty(@event.Channel) ? _targetChannelProvider.Current.Login : @event.Channel;
        var streamId = NullIfEmpty(@event.Stream?.Id);
        var initialViewers = @event.Stream?.ViewerCount ?? 0;
        var initialTitle = NullIfEmpty(@event.Stream?.Title);
        var initialGame = NullIfEmpty(@event.Stream?.GameName);
        var updated = false;

        lock (_stateLock)
        {
            if (_activeBroadcastId != @event.BroadcastId)
            {
                return Task.CompletedTask;
            }

            if (_platforms.TryGetValue(@event.Platform, out var existing))
            {
                ReopenPlatformCollector(existing, channel, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
            }
            else
            {
                AddFreshPlatformCollector(@event.Platform, channel, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
            }

            updated = true;
        }

        if (updated)
        {
            Checkpoint();
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastPlatformLeft @event, CancellationToken cancellationToken)
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            return Task.CompletedTask;
        }

        var updated = false;

        lock (_stateLock)
        {
            if (_activeBroadcastId != @event.BroadcastId || !_platforms.TryGetValue(@event.Platform, out var collector))
            {
                return Task.CompletedTask;
            }

            ClosePlatformCollector(collector, @event.LeftAt);
            updated = true;
        }

        if (updated)
        {
            Checkpoint();
        }

        return Task.CompletedTask;
    }

    public async Task HandleAsync(BroadcastEnded @event, CancellationToken cancellationToken)
    {
        await StopSamplingLoopAsync().ConfigureAwait(false);

        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            ClearMemoryState();
            return;
        }

        var completedDraft = TakeCompletedDraft(@event.BroadcastId, @event.EndedAt);

        if (completedDraft is null)
        {
            var orphanRecord = _draftCoordinator.TryFinalizeOrphanDraft(@event.LastPlatform, @event.EndedAt);
            if (orphanRecord is not null)
            {
                await _eventBus.PublishAsync(new StreamSessionCompleted(orphanRecord), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var record = _draftCoordinator.FinalizeCompletedDraft(completedDraft);

        await _eventBus.PublishAsync(new StreamSessionCompleted(record), cancellationToken).ConfigureAwait(false);

        var duration = record.Duration;

        _logger.LogInformation(
            "Эфир {BroadcastId} завершён и сохранён: длительность {Duration}, площадок {Platforms}, сообщений {Messages}, чаттеров {Chatters}, пик {Peak}, средний {Avg}",
            record.Id,
            duration,
            record.Platforms.Count,
            record.MessageCount,
            record.ChatterCount,
            record.PeakViewers,
            record.AverageViewers);

        SendEndStatsMessage(record, @event.LastChannel);
    }

    public async Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        if (!@event.IsCatchUp || !_targetChannelProvider.Current.RecordsUserData)
        {
            return;
        }

        bool hasMemorySession;
        lock (_stateLock)
        {
            hasMemorySession = _activeBroadcastId is not null;
        }

        if (hasMemorySession || _broadcastState.IsAnyLive)
        {
            _logger.LogDebug("Catch-up offline при активном эфире в памяти – событие проигнорировано");
            return;
        }

        var orphanRecord = _draftCoordinator.TryFinalizeOrphanDraft(@event.Platform, _timeProvider.GetUtcNow());
        if (orphanRecord is not null)
        {
            await _eventBus.PublishAsync(new StreamSessionCompleted(orphanRecord), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Восстановлен эфир из черновика (catch-up offline {Platform}): канал {Channel}, сообщений {Messages}, чаттеров {Chatters}, длительность {Duration}",
                @event.Platform,
                orphanRecord.Channel,
                orphanRecord.MessageCount,
                orphanRecord.ChatterCount,
                orphanRecord.Duration);
        }
    }

    public Task HandleAsync(StreamMetadataResolved @event, CancellationToken cancellationToken)
    {
        bool identityChanged;

        lock (_stateLock)
        {
            if (_activeBroadcastId is null
                || !_platforms.TryGetValue(@event.Platform, out var collector)
                || IsForeignStream(collector, @event.Stream.Id))
            {
                return Task.CompletedTask;
            }

            identityChanged = AdoptCanonicalIdentity(collector, @event.Stream);
            ApplyMetadata(collector, @event.Stream.Title, @event.Stream.GameName, _timeProvider.GetUtcNow());

            var current = collector.Segments[^1];
            if (@event.Stream.ViewerCount > current.PeakViewers)
            {
                current.PeakViewers = @event.Stream.ViewerCount;
            }
        }

        if (identityChanged)
        {
            Checkpoint();
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ViewerSampleObserved @event, CancellationToken cancellationToken)
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            if (_activeBroadcastId is null
                || !_platforms.TryGetValue(@event.Platform, out var collector)
                || collector.EndedAt is not null
                || IsForeignStream(collector, @event.StreamId))
            {
                return Task.CompletedTask;
            }

            if (collector.StreamId is null && !string.IsNullOrEmpty(@event.StreamId))
            {
                collector.StreamId = @event.StreamId;
            }

            var current = collector.Segments[^1];
            if (@event.ViewerCount > current.PeakViewers)
            {
                current.PeakViewers = @event.ViewerCount;
            }

            var isDuplicateSeed = collector.InitialSeedPending
                                  && collector.LastViewerCount == @event.ViewerCount
                                  && (@event.SampledAtUtc - collector.InitialSeedAtUtc).Duration() <= InitialSeedDedupWindow;

            collector.InitialSeedPending = false;
            collector.LastViewerCount = @event.ViewerCount;
            collector.LastViewerSampleAt = @event.SampledAtUtc;

            if (!isDuplicateSeed && @event.ViewerCount >= 0)
            {
                current.ViewerSamplesSum += @event.ViewerCount;
                current.ViewerSamplesCount++;
            }
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.IsBot || !_targetChannelProvider.Current.RecordsUserData)
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            if (_activeBroadcastId is null
                || !_platforms.TryGetValue(@event.Platform, out var collector)
                || collector.EndedAt is not null)
            {
                return Task.CompletedTask;
            }

            collector.Segments[^1].MessageCount++;

            if (!string.IsNullOrEmpty(@event.UserId))
            {
                if (!collector.Chatters.TryGetValue(@event.UserId, out var tally))
                {
                    tally = new ChatterTally();
                    collector.Chatters[@event.UserId] = tally;
                }

                tally.MessageCount++;

                var displayName = FirstNonEmpty(@event.DisplayName, @event.Username);
                if (displayName is not null)
                {
                    tally.DisplayName = displayName;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ChannelUpdated @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_activeBroadcastId is null
                || !_platforms.TryGetValue(@event.Platform, out var collector)
                || collector.EndedAt is not null)
            {
                return Task.CompletedTask;
            }

            ApplyMetadata(collector, @event.Title, @event.GameName, _timeProvider.GetUtcNow());
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatIngestionStarted @event, CancellationToken cancellationToken)
    {
        var shouldCheckpoint = false;

        lock (_stateLock)
        {
            if (_ingestionSinceByPlatform.ContainsKey(@event.Platform))
            {
                return Task.CompletedTask;
            }

            _ingestionSinceByPlatform[@event.Platform] = @event.At;

            if (_activeBroadcastId is not null
                && _platforms.TryGetValue(@event.Platform, out var collector)
                && collector.EndedAt is null)
            {
                OpenTracked(collector, @event.At);
                shouldCheckpoint = true;
            }
        }

        if (shouldCheckpoint)
        {
            Checkpoint();
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatIngestionStopped @event, CancellationToken cancellationToken)
    {
        var shouldCheckpoint = false;

        lock (_stateLock)
        {
            if (!_ingestionSinceByPlatform.TryGetValue(@event.Platform, out var since) || @event.At < since)
            {
                return Task.CompletedTask;
            }

            _ingestionSinceByPlatform.Remove(@event.Platform);

            if (_activeBroadcastId is not null && _platforms.TryGetValue(@event.Platform, out var collector))
            {
                CloseOpenTracked(collector, @event.At);
                shouldCheckpoint = true;
            }
        }

        if (shouldCheckpoint)
        {
            Checkpoint();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        try
        {
            await StopSamplingLoopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Ошибка остановки цикла семплирования зрителей при DisposeAsync");
        }

        Checkpoint();
    }

    private void AddFreshPlatformCollector(
        StreamingPlatform platform,
        string channel,
        string? streamId,
        DateTimeOffset startedAt,
        string? title,
        string? game,
        int initialViewers,
        DateTimeOffset now)
    {
        var collector = new PlatformCollector
        {
            Platform = platform,
            Channel = channel,
            StreamId = streamId,
            StartedAt = startedAt,
            TrackedKnown = true,
        };

        collector.Segments.Add(new SegmentTally
        {
            StartedAt = startedAt,
            Title = title,
            Game = game,
            PeakViewers = initialViewers,
            ViewerSamplesSum = initialViewers > 0 ? initialViewers : 0,
            ViewerSamplesCount = initialViewers > 0 ? 1 : 0,
        });

        if (initialViewers > 0)
        {
            collector.LastViewerCount = initialViewers;
            collector.LastViewerSampleAt = now;
            collector.InitialSeedPending = true;
            collector.InitialSeedAtUtc = now;
        }

        if (_ingestionSinceByPlatform.TryGetValue(platform, out var ingestionSince))
        {
            OpenTracked(collector, ingestionSince);
        }

        _platforms[platform] = collector;

        if (_broadcastStartedAt is null || startedAt < _broadcastStartedAt.Value)
        {
            _broadcastStartedAt = startedAt;
        }
    }

    private void ReopenPlatformCollector(
        PlatformCollector collector,
        string channel,
        string? streamId,
        DateTimeOffset startedAt,
        string? title,
        string? game,
        int initialViewers,
        DateTimeOffset now)
    {
        collector.Channel = channel;
        collector.StreamId = streamId ?? collector.StreamId;
        collector.EndedAt = null;

        var lastSegment = collector.Segments[^1];
        if (lastSegment.EndedAt is not null)
        {
            collector.Segments.Add(new SegmentTally
            {
                StartedAt = startedAt,
                Title = title ?? lastSegment.Title,
                Game = game ?? lastSegment.Game,
                PeakViewers = initialViewers,
                ViewerSamplesSum = initialViewers > 0 ? initialViewers : 0,
                ViewerSamplesCount = initialViewers > 0 ? 1 : 0,
            });
        }
        else
        {
            ApplyMetadata(collector, title, game, now);
            if (initialViewers > lastSegment.PeakViewers)
            {
                lastSegment.PeakViewers = initialViewers;
            }
        }

        if (initialViewers > 0)
        {
            collector.LastViewerCount = initialViewers;
            collector.LastViewerSampleAt = now;
            collector.InitialSeedPending = true;
            collector.InitialSeedAtUtc = now;
        }

        if (_ingestionSinceByPlatform.TryGetValue(collector.Platform, out var ingestionSince))
        {
            OpenTracked(collector, ingestionSince);
        }
    }

    private static PlatformCollector RestorePlatformCollector(ActiveBroadcastPlatform draft)
    {
        var collector = new PlatformCollector
        {
            Platform = draft.Platform,
            Channel = draft.Channel,
            StreamId = draft.StreamId,
            StartedAt = draft.StartedAt,
            EndedAt = draft.EndedAt,
            LastViewerCount = draft.LastViewerCount,
            LastViewerSampleAt = draft.LastViewerSampleAt,
            TrackedKnown = draft.TrackedIntervals is not null,
        };

        if (draft.Segments.Count > 0)
        {
            foreach (var segment in draft.Segments)
            {
                collector.Segments.Add(new SegmentTally
                {
                    StartedAt = segment.StartedAt,
                    EndedAt = segment.EndedAt,
                    Title = segment.Title,
                    Game = segment.Game,
                    MessageCount = segment.MessageCount,
                    PeakViewers = segment.PeakViewers,
                    ViewerSamplesSum = segment.ViewerSamplesSum,
                    ViewerSamplesCount = segment.ViewerSamplesCount,
                });
            }
        }
        else
        {
            collector.Segments.Add(new SegmentTally
            {
                StartedAt = draft.StartedAt,
                EndedAt = draft.EndedAt,
                Title = draft.Title,
                Game = draft.Game,
                PeakViewers = draft.PeakViewers,
                ViewerSamplesSum = draft.ViewerSamplesSum,
                ViewerSamplesCount = draft.ViewerSamplesCount,
            });
        }

        if (draft.TrackedIntervals is not null)
        {
            foreach (var interval in draft.TrackedIntervals)
            {
                collector.TrackedIntervals.Add(new ActiveStreamSessionInterval
                {
                    StartedAt = interval.StartedAt,
                    EndedAt = interval.EndedAt ?? (draft.UpdatedAt > interval.StartedAt ? draft.UpdatedAt : interval.StartedAt),
                });
            }
        }

        foreach (var chatter in draft.Chatters)
        {
            collector.Chatters[chatter.UserId] = new ChatterTally
            {
                DisplayName = chatter.DisplayName,
                MessageCount = chatter.MessageCount,
            };
        }

        return collector;
    }

    private static void ClosePlatformCollector(PlatformCollector collector, DateTimeOffset closedAt)
    {
        var effectiveEnd = closedAt < collector.StartedAt ? collector.StartedAt : closedAt;
        collector.EndedAt = effectiveEnd;

        if (collector.Segments.Count > 0 && collector.Segments[^1].EndedAt is null)
        {
            collector.Segments[^1].EndedAt = effectiveEnd;
        }

        CloseOpenTracked(collector, effectiveEnd);
    }

    private ActiveBroadcast? TakeCompletedDraft(Guid broadcastId, DateTimeOffset endedAt)
    {
        lock (_stateLock)
        {
            if (_activeBroadcastId != broadcastId)
            {
                return null;
            }

            foreach (var collector in _platforms.Values)
            {
                if (collector.EndedAt is null)
                {
                    ClosePlatformCollector(collector, endedAt);
                }
            }

            if (_viewerSeries.Count == 0 && _platforms.Count > 1)
            {
                AppendCombinedSampleAtLocked(endedAt, includeEnded: true);
            }

            var completed = CreateDraftSnapshotLocked(endedAt);

            _activeBroadcastId = null;
            _broadcastStartedAt = null;
            _nextSampleTickUtc = null;
            _platforms.Clear();
            _viewerSeries.Clear();

            return completed;
        }
    }

    private void ClearMemoryState()
    {
        lock (_stateLock)
        {
            _activeBroadcastId = null;
            _broadcastStartedAt = null;
            _nextSampleTickUtc = null;
            _platforms.Clear();
            _viewerSeries.Clear();
        }
    }

    private ActiveBroadcast? CreateDraftSnapshotLocked(DateTimeOffset? endedAt = null)
    {
        if (_activeBroadcastId is null || _broadcastStartedAt is null || _platforms.Count == 0)
        {
            return null;
        }

        var now = endedAt ?? _timeProvider.GetUtcNow();
        var platformDrafts = new List<ActiveBroadcastPlatform>(_platforms.Count);

        foreach (var collector in _platforms.Values)
        {
            var segments = collector.Segments
                .Select(segment => new ActiveStreamSessionSegment
                {
                    StartedAt = segment.StartedAt,
                    EndedAt = segment.EndedAt,
                    Title = segment.Title,
                    Game = segment.Game,
                    MessageCount = segment.MessageCount,
                    PeakViewers = segment.PeakViewers,
                    ViewerSamplesSum = segment.ViewerSamplesSum,
                    ViewerSamplesCount = segment.ViewerSamplesCount,
                })
                .ToList();

            var lastSegment = segments[^1];

            platformDrafts.Add(new ActiveBroadcastPlatform
            {
                Platform = collector.Platform,
                Channel = collector.Channel,
                StreamId = collector.StreamId,
                StartedAt = collector.StartedAt,
                UpdatedAt = collector.EndedAt ?? now,
                EndedAt = collector.EndedAt,
                Title = lastSegment.Title,
                Game = lastSegment.Game,
                PeakViewers = segments.Max(static s => s.PeakViewers),
                ViewerSamplesSum = segments.Sum(static s => s.ViewerSamplesSum),
                ViewerSamplesCount = segments.Sum(static s => s.ViewerSamplesCount),
                LastViewerCount = collector.LastViewerCount,
                LastViewerSampleAt = collector.LastViewerSampleAt,
                Chatters =
                [
                    .. collector.Chatters.Select(pair => new ActiveStreamSessionChatter
                    {
                        UserId = pair.Key,
                        DisplayName = pair.Value.DisplayName,
                        MessageCount = pair.Value.MessageCount,
                    }),
                ],
                Segments = segments,
                TrackedIntervals = collector.TrackedKnown
                    ? collector.TrackedIntervals
                        .Select(static i => new ActiveStreamSessionInterval
                        {
                            StartedAt = i.StartedAt,
                            EndedAt = i.EndedAt,
                        })
                        .ToList()
                    : null,
            });
        }

        return new ActiveBroadcast
        {
            BroadcastId = _activeBroadcastId.Value,
            StartedAt = _broadcastStartedAt.Value,
            UpdatedAt = now,
            EndedAt = endedAt,
            NextSampleTickUtc = _nextSampleTickUtc,
            ViewerSeries = [.. _viewerSeries],
            Platforms = platformDrafts,
        };
    }

    private void Checkpoint()
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            return;
        }

        ActiveBroadcast? snapshot;

        lock (_stateLock)
        {
            snapshot = CreateDraftSnapshotLocked();
        }

        if (snapshot is null)
        {
            return;
        }

        try
        {
            _activeBroadcastStore.TrySave(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Не удалось сохранить чекпойнт активного эфира {BroadcastId}",
                snapshot.BroadcastId);
        }
    }

    private void StartSamplingLoop()
    {
        lock (_stateLock)
        {
            var newCts = new CancellationTokenSource();
            var token = newCts.Token;
            _samplingCts?.Dispose();
            _samplingCts = newCts;
            _samplingTask = Task.Run(() => RunSamplingLoopAsync(token), token);
        }
    }

    private async Task StopSamplingLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_stateLock)
        {
            cts = _samplingCts;
            task = _samplingTask;
            _samplingCts = null;
            _samplingTask = null;
        }

        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Цикл семплирования зрителей завершился с ошибкой");
            }
        }

        cts?.Dispose();
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset targetTick;
                TimeSpan delay;

                lock (_stateLock)
                {
                    if (_activeBroadcastId is null)
                    {
                        return;
                    }

                    var now = _timeProvider.GetUtcNow();
                    targetTick = AdvanceTickCursorAfter(_nextSampleTickUtc, now);
                    _nextSampleTickUtc = targetTick;
                    delay = targetTick - now;
                }

                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);

                lock (_stateLock)
                {
                    if (_activeBroadcastId is null)
                    {
                        return;
                    }

                    AppendCombinedSampleAtLocked(targetTick, includeEnded: false);
                    _nextSampleTickUtc = targetTick + ViewerSampleInterval;
                }

                Checkpoint();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AppendCombinedSampleAtLocked(DateTimeOffset tickUtc, bool includeEnded)
    {
        var sum = 0;
        var hasFreshSample = false;

        foreach (var collector in _platforms.Values)
        {
            if (!includeEnded && collector.EndedAt is not null)
            {
                continue;
            }

            if (collector.LastViewerCount is not { } count || collector.LastViewerSampleAt is not { } sampledAt)
            {
                continue;
            }

            var delta = tickUtc - sampledAt;
            if (delta < -FutureSampleTolerance || delta > ViewerSampleInterval)
            {
                continue;
            }

            sum += count;
            hasFreshSample = true;
        }

        if (hasFreshSample)
        {
            _viewerSeries.Add(new BroadcastViewerSample(tickUtc, sum));
        }
    }

    private static DateTimeOffset AdvanceTickCursorAfter(DateTimeOffset? existingCursor, DateTimeOffset now)
    {
        if (existingCursor is not { } cursor)
        {
            return now + ViewerSampleInterval;
        }

        while (cursor <= now)
        {
            cursor += ViewerSampleInterval;
        }

        return cursor;
    }

    private void ApplyMetadata(PlatformCollector collector, string? title, string? game, DateTimeOffset now)
    {
        var current = collector.Segments[^1];

        var nextGame = string.IsNullOrEmpty(game) ? current.Game : game;
        var nextTitle = string.IsNullOrEmpty(title) ? current.Title : title;

        var gameChanged = !string.IsNullOrEmpty(game)
                          && !string.IsNullOrEmpty(current.Game)
                          && !string.Equals(current.Game, game, StringComparison.OrdinalIgnoreCase);

        var titleChanged = !string.IsNullOrEmpty(title)
                           && !string.IsNullOrEmpty(current.Title)
                           && !string.Equals(current.Title, title, StringComparison.Ordinal);

        if (!gameChanged && !titleChanged)
        {
            current.Game = nextGame;
            current.Title = nextTitle;
            return;
        }

        current.EndedAt = now;

        collector.Segments.Add(new SegmentTally
        {
            StartedAt = now,
            Title = nextTitle,
            Game = nextGame,
        });

        _logger.LogInformation(
            "Метаданные стрима ({Platform}) изменены: игра \"{Game}\", название \"{Title}\" – открыт сегмент {Count}",
            collector.Platform,
            nextGame,
            nextTitle,
            collector.Segments.Count);
    }

    private bool IsForeignStream(PlatformCollector collector, string? rawStreamId)
    {
        var streamId = NullIfEmpty(rawStreamId);

        if (streamId is null || collector.StreamId is null || string.Equals(collector.StreamId, streamId, StringComparison.Ordinal))
        {
            return false;
        }

        _logger.LogWarning(
            "Метаданные относятся к другому стриму ({ResolvedId} вместо {SessionId}) – в статистику не попадают",
            streamId,
            collector.StreamId);

        return true;
    }

    private bool AdoptCanonicalIdentity(PlatformCollector collector, StreamInfo stream)
    {
        var streamId = NullIfEmpty(stream.Id);
        var changed = streamId is not null && collector.StreamId is null;

        if (changed)
        {
            collector.StreamId = streamId;
        }

        if (stream.StartedAt == default)
        {
            return changed;
        }

        var canonical = new DateTimeOffset(DateTime.SpecifyKind(stream.StartedAt, DateTimeKind.Utc));

        if (canonical == collector.StartedAt)
        {
            return changed;
        }

        _logger.LogInformation(
            "Начало сессии статистики ({Platform}) уточнено: {PreviousStart} → {CanonicalStart}",
            collector.Platform,
            collector.StartedAt,
            canonical);

        collector.Segments[0].StartedAt = canonical;
        collector.StartedAt = canonical;

        if (_broadcastStartedAt is null || canonical < _broadcastStartedAt.Value)
        {
            _broadcastStartedAt = canonical;
        }

        return true;
    }

    private void SendEndStatsMessage(BroadcastRecord record, string fallbackChannel)
    {
        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (!settings.StreamStatusNotificationsEnabled
            || string.IsNullOrWhiteSpace(settings.StreamEndStatsMessage))
        {
            return;
        }

        var channel = string.IsNullOrEmpty(record.Channel) ? fallbackChannel : record.Channel;
        var duration = record.Duration;

        var message = MessageTemplate.For(settings.StreamEndStatsMessage)
            .With("duration", FormattingUtils.FormatTimeSpan(duration))
            .With("messages", FormattingUtils.FormatNumber(record.MessageCount))
            .With("chatters", FormattingUtils.FormatNumber(record.ChatterCount))
            .With("peakViewers", FormattingUtils.FormatNumber(record.PeakViewers))
            .With("avgViewers", FormattingUtils.FormatNumber(record.AverageViewers))
            .With("title", record.Title ?? MissingValuePlaceholder)
            .With("game", record.Game ?? MissingValuePlaceholder)
            .With("channel", channel)
            .Render();

        _logger.LogInformation(
            "Отправка итоговой статистики эфира {BroadcastId}: длительность {Duration}, сообщений {Messages}, чаттеров {Chatters}, пик {Peak}, средний {Avg}",
            record.Id,
            duration,
            record.MessageCount,
            record.ChatterCount,
            record.PeakViewers,
            record.AverageViewers);

        _messenger.SendToConnected(message);
    }

    private static void OpenTracked(PlatformCollector collector, DateTimeOffset at)
    {
        if (collector.TrackedIntervals.Count > 0 && collector.TrackedIntervals[^1].EndedAt is null)
        {
            return;
        }

        collector.TrackedIntervals.Add(new ActiveStreamSessionInterval { StartedAt = at });
    }

    private static void CloseOpenTracked(PlatformCollector collector, DateTimeOffset at)
    {
        foreach (var interval in collector.TrackedIntervals)
        {
            interval.EndedAt ??= at;
        }
    }

    private static DateTimeOffset ResolveStartedAt(StreamInfo? stream, DateTimeOffset fallback)
    {
        return stream?.StartedAt is { } streamStart && streamStart != default
            ? new DateTimeOffset(DateTime.SpecifyKind(streamStart, DateTimeKind.Utc))
            : fallback;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        return NullIfEmpty(primary) ?? NullIfEmpty(fallback);
    }

    private sealed class PlatformCollector
    {
        public StreamingPlatform Platform { get; init; }
        public string Channel { get; set; } = string.Empty;
        public string? StreamId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? LastViewerCount { get; set; }
        public DateTimeOffset? LastViewerSampleAt { get; set; }
        public bool InitialSeedPending { get; set; }
        public DateTimeOffset InitialSeedAtUtc { get; set; }
        public bool TrackedKnown { get; set; }
        public Dictionary<string, ChatterTally> Chatters { get; } = new(StringComparer.Ordinal);
        public List<SegmentTally> Segments { get; } = [];
        public List<ActiveStreamSessionInterval> TrackedIntervals { get; } = [];
    }

    private sealed class SegmentTally
    {
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public string? Title { get; set; }
        public string? Game { get; set; }
        public long MessageCount { get; set; }
        public int PeakViewers { get; set; }
        public long ViewerSamplesSum { get; set; }
        public int ViewerSamplesCount { get; set; }
    }

    private sealed class ChatterTally
    {
        public string DisplayName { get; set; } = string.Empty;
        public long MessageCount { get; set; }
    }
}
