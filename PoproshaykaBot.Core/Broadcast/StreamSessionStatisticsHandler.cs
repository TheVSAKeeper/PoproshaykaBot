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
    private readonly List<PlatformCollector> _collectors = [];
    private readonly Dictionary<StreamingPlatform, (string? ChannelId, DateTimeOffset At)> _ingestionSinceByPlatform = [];
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
        var channelId = ResolveChannelId(@event.ChannelId, @event.Stream);
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
            _collectors.Clear();
            _viewerSeries.Clear();
            _activeBroadcastId = @event.BroadcastId;

            if (resume && existingDraft is not null)
            {
                _broadcastStartedAt = existingDraft.StartedAt != default ? existingDraft.StartedAt : startedAt;
                _viewerSeries.AddRange(existingDraft.ViewerSeries);
                _nextSampleTickUtc = AdvanceTickCursorAfter(existingDraft.NextSampleTickUtc, now);

                var resumedFirstCollector = false;

                foreach (var platformDraft in existingDraft.Platforms)
                {
                    if (platformDraft.StartedAt == default || string.IsNullOrEmpty(platformDraft.Channel))
                    {
                        continue;
                    }

                    var collector = RestorePlatformCollector(platformDraft);
                    var isFirstChannel = MatchesChannel(collector, @event.FirstPlatform, channelId, channel);
                    if (!isFirstChannel && collector.EndedAt is null)
                    {
                        ClosePlatformCollector(collector, collector.UpdatedAt);
                    }

                    _collectors.Add(collector);

                    if (isFirstChannel)
                    {
                        resumedFirstCollector = true;
                        collector.ChannelId ??= channelId;
                        collector.StreamId = streamId ?? collector.StreamId;
                        ApplyMetadata(collector, initialTitle, initialGame, now);

                        var current = collector.Segments[^1];
                        if (initialViewers > current.PeakViewers)
                        {
                            current.PeakViewers = initialViewers;
                        }
                    }

                    if (collector.EndedAt is null && TryGetIngestionSince(collector.Platform, collector.ChannelId, out var ingestionSince))
                    {
                        OpenTracked(collector, ingestionSince);
                    }
                }

                if (!resumedFirstCollector)
                {
                    AddFreshPlatformCollector(@event.FirstPlatform, channel, channelId, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
                }
            }
            else
            {
                _broadcastStartedAt = startedAt;
                _nextSampleTickUtc = now + ViewerSampleInterval;
                AddFreshPlatformCollector(@event.FirstPlatform, channel, channelId, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
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
        var channelId = ResolveChannelId(@event.ChannelId, @event.Stream);
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

            var existing = FindCollectorLocked(@event.Platform, channelId, channel, openOnly: false);
            if (existing is not null && (existing.EndedAt is not null || !HasDifferentStreamId(existing, streamId)))
            {
                ReopenPlatformCollector(existing, channel, channelId, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
            }
            else
            {
                if (existing is { EndedAt: null })
                {
                    ClosePlatformCollector(existing, startedAt < now ? startedAt : now);
                }

                AddFreshPlatformCollector(@event.Platform, channel, channelId, streamId, startedAt, initialTitle, initialGame, initialViewers, now);
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
            var collector = _activeBroadcastId == @event.BroadcastId
                ? FindCollectorLocked(@event.Platform, @event.ChannelId, null, openOnly: true)
                : null;

            if (collector is null)
            {
                return Task.CompletedTask;
            }

            ClosePlatformCollector(collector, @event.IsCatchUp ? collector.UpdatedAt : @event.LeftAt);
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

        var completedDraft = TakeCompletedDraft(@event.BroadcastId, @event.EndedAt, @event.IsCatchUp);

        if (completedDraft is null)
        {
            var orphanRecord = _draftCoordinator.TryFinalizeOrphanDraft(@event.LastPlatform, @event.IsCatchUp ? null : @event.EndedAt);
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

        if (!@event.IsCatchUp)
        {
            SendEndStatsMessage(record, @event.LastChannel);
        }
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

        var orphanRecord = _draftCoordinator.TryFinalizeOrphanDraft(@event.Platform);
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
            var collector = _activeBroadcastId is not null
                ? FindCollectorLocked(@event.Platform, ResolveChannelId(@event.ChannelId, @event.Stream), @event.Channel, openOnly: true)
                : null;

            if (collector is null || IsForeignStream(collector, @event.Stream.Id))
            {
                return Task.CompletedTask;
            }

            identityChanged = AdoptCanonicalIdentity(collector, @event.Stream, @event.ChannelId);
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
            var collector = _activeBroadcastId is not null
                ? FindCollectorLocked(@event.Platform, @event.ChannelId, @event.Channel, openOnly: true)
                : null;

            if (collector is null || IsForeignStream(collector, @event.StreamId))
            {
                return Task.CompletedTask;
            }

            if (collector.ChannelId is null && !string.IsNullOrWhiteSpace(@event.ChannelId))
            {
                collector.ChannelId = @event.ChannelId.Trim();
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
            if (@event.SampledAtUtc > collector.UpdatedAt)
            {
                collector.UpdatedAt = @event.SampledAtUtc;
            }

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
        if (@event.IsBot || !(@event.RecordsUserData ?? _targetChannelProvider.Current.RecordsUserData))
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            if (@event.Origin is { IsCurrent: false } || _activeBroadcastId is null)
            {
                return Task.CompletedTask;
            }

            var collector = FindCollectorLocked(
                @event.Platform,
                @event.ChannelKey?.ChannelId,
                @event.Channel,
                openOnly: true);

            if (collector is null)
            {
                return Task.CompletedTask;
            }

            if (collector.ChannelId is null && @event.ChannelKey is { } channelKey)
            {
                collector.ChannelId = channelKey.ChannelId;
            }

            collector.Segments[^1].MessageCount++;
            collector.UpdatedAt = _timeProvider.GetUtcNow();

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
            var collector = _activeBroadcastId is not null
                ? FindCollectorLocked(@event.Platform, @event.ChannelId, null, openOnly: true)
                : null;

            if (collector is null)
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
            if (_ingestionSinceByPlatform.TryGetValue(@event.Platform, out var existing)
                && string.Equals(existing.ChannelId, @event.ChannelId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            _ingestionSinceByPlatform[@event.Platform] = (@event.ChannelId, @event.At);

            if (_activeBroadcastId is not null
                && FindCollectorLocked(@event.Platform, @event.ChannelId, null, openOnly: true) is { } collector)
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
            if (!_ingestionSinceByPlatform.TryGetValue(@event.Platform, out var since) || @event.At < since.At)
            {
                return Task.CompletedTask;
            }

            _ingestionSinceByPlatform.Remove(@event.Platform);

            if (_activeBroadcastId is not null
                && FindCollectorLocked(@event.Platform, @event.ChannelId, null, openOnly: true) is { } collector)
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
        string? channelId,
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
            ChannelId = channelId,
            StreamId = streamId,
            StartedAt = startedAt,
            TrackedKnown = true,
            UpdatedAt = now,
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

        if (TryGetIngestionSince(platform, channelId, out var ingestionSince))
        {
            OpenTracked(collector, ingestionSince);
        }

        _collectors.Add(collector);

        if (_broadcastStartedAt is null || startedAt < _broadcastStartedAt.Value)
        {
            _broadcastStartedAt = startedAt;
        }
    }

    private void ReopenPlatformCollector(
        PlatformCollector collector,
        string channel,
        string? channelId,
        string? streamId,
        DateTimeOffset startedAt,
        string? title,
        string? game,
        int initialViewers,
        DateTimeOffset now)
    {
        collector.Channel = channel;
        collector.ChannelId ??= channelId;
        collector.StreamId = streamId ?? collector.StreamId;
        collector.EndedAt = null;
        collector.UpdatedAt = now;

        var lastSegment = collector.Segments[^1];
        if (lastSegment.EndedAt is { } previousEnd)
        {
            collector.Segments.Add(new SegmentTally
            {
                StartedAt = startedAt < previousEnd ? previousEnd : startedAt,
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

        if (TryGetIngestionSince(collector.Platform, collector.ChannelId, out var ingestionSince))
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
            ChannelId = draft.ChannelId,
            StreamId = draft.StreamId,
            StartedAt = draft.StartedAt,
            UpdatedAt = draft.UpdatedAt,
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

    private ActiveBroadcast? TakeCompletedDraft(Guid broadcastId, DateTimeOffset endedAt, bool isCatchUp)
    {
        lock (_stateLock)
        {
            if (_activeBroadcastId != broadcastId)
            {
                return null;
            }

            foreach (var collector in _collectors)
            {
                if (collector.EndedAt is null)
                {
                    ClosePlatformCollector(collector, isCatchUp ? collector.UpdatedAt : endedAt);
                }
            }

            if (isCatchUp)
            {
                endedAt = _collectors.Max(static collector => collector.EndedAt!.Value);
                _viewerSeries.RemoveAll(sample => sample.At > endedAt);
            }

            if (_viewerSeries.Count == 0 && _collectors.Count > 1)
            {
                AppendCombinedSampleAtLocked(endedAt, includeEnded: true);
            }

            var completed = CreateDraftSnapshotLocked(endedAt);

            _activeBroadcastId = null;
            _broadcastStartedAt = null;
            _nextSampleTickUtc = null;
            _collectors.Clear();
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
            _collectors.Clear();
            _viewerSeries.Clear();
        }
    }

    private ActiveBroadcast? CreateDraftSnapshotLocked(DateTimeOffset? endedAt = null)
    {
        if (_activeBroadcastId is null || _broadcastStartedAt is null || _collectors.Count == 0)
        {
            return null;
        }

        var platformDrafts = new List<ActiveBroadcastPlatform>(_collectors.Count);

        foreach (var collector in _collectors)
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
                ChannelId = collector.ChannelId,
                StreamId = collector.StreamId,
                StartedAt = collector.StartedAt,
                UpdatedAt = collector.EndedAt ?? collector.UpdatedAt,
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
            UpdatedAt = endedAt ?? platformDrafts.Max(static platform => platform.UpdatedAt),
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

        lock (_stateLock)
        {
            var snapshot = CreateDraftSnapshotLocked();
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

        foreach (var collector in _collectors)
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
        if (now > collector.UpdatedAt)
        {
            collector.UpdatedAt = now;
        }

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

    private bool AdoptCanonicalIdentity(PlatformCollector collector, StreamInfo stream, string? eventChannelId = null)
    {
        var changed = false;
        var resolvedChannelId = ResolveChannelId(eventChannelId, stream);
        if (resolvedChannelId is not null && collector.ChannelId is null)
        {
            collector.ChannelId = resolvedChannelId;
            changed = true;
        }

        var streamId = NullIfEmpty(stream.Id);
        if (streamId is not null && collector.StreamId is null)
        {
            collector.StreamId = streamId;
            changed = true;
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

        _messenger.SendToConnected(message, ChatSendCategory.EventNotification);
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

    private PlatformCollector? FindCollectorLocked(
        StreamingPlatform platform,
        string? channelId,
        string? channel,
        bool openOnly)
    {
        var hasIdentity = !string.IsNullOrWhiteSpace(channelId) || !string.IsNullOrWhiteSpace(channel);
        PlatformCollector? fallback = null;

        for (var i = _collectors.Count - 1; i >= 0; i--)
        {
            var candidate = _collectors[i];
            if (candidate.Platform != platform)
            {
                continue;
            }

            if (openOnly && candidate.EndedAt is not null)
            {
                continue;
            }

            if (hasIdentity && MatchesChannel(candidate, platform, channelId, channel))
            {
                return candidate;
            }

            if (!hasIdentity)
            {
                fallback ??= candidate;
            }
        }

        return fallback;
    }

    private bool TryGetIngestionSince(StreamingPlatform platform, string? channelId, out DateTimeOffset since)
    {
        if (_ingestionSinceByPlatform.TryGetValue(platform, out var entry)
            && (entry.ChannelId is null
                || channelId is null
                || string.Equals(entry.ChannelId, channelId, StringComparison.Ordinal)))
        {
            since = entry.At;
            return true;
        }

        since = default;
        return false;
    }

    private static bool MatchesChannel(
        PlatformCollector collector,
        StreamingPlatform platform,
        string? channelId,
        string? channel)
    {
        if (collector.Platform != platform)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(collector.ChannelId) && !string.IsNullOrWhiteSpace(channelId))
        {
            return string.Equals(collector.ChannelId, channelId, StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(channel)
            && string.Equals(collector.Channel, channel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDifferentStreamId(PlatformCollector collector, string? streamId)
    {
        return !string.IsNullOrEmpty(collector.StreamId)
            && !string.IsNullOrEmpty(streamId)
            && !string.Equals(collector.StreamId, streamId, StringComparison.Ordinal);
    }

    private static string? ResolveChannelId(string? eventChannelId, StreamInfo? stream)
    {
        return NullIfEmpty(eventChannelId?.Trim()) ?? NullIfEmpty(stream?.UserId?.Trim());
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
        public string? ChannelId { get; set; }
        public string? StreamId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
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
