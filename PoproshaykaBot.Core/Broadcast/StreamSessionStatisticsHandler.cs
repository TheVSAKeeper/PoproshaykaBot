using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class StreamSessionStatisticsHandler :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventHandler<StreamMetadataResolved>,
    IEventHandler<ChatMessageReceived>,
    IEventHandler<ChannelUpdated>,
    IEventSubscriber,
    IAsyncDisposable
{
    private const string MissingValuePlaceholder = "—";
    private static readonly TimeSpan ViewerSampleInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan StreamMatchTolerance = TimeSpan.FromSeconds(5);

    private readonly IChatMessenger _messenger;
    private readonly IStreamStatus _streamStatus;
    private readonly SettingsManager _settingsManager;
    private readonly TimeProvider _timeProvider;
    private readonly IEventBus _eventBus;
    private readonly ActiveStreamSessionStore _activeSessionStore;
    private readonly ILogger<StreamSessionStatisticsHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly IDisposable _metadataResolvedSubscription;
    private readonly IDisposable _chatSubscription;
    private readonly IDisposable _channelUpdatedSubscription;

    private readonly object _stateLock = new();
    private readonly Dictionary<string, ChatterTally> _chatters = new(StringComparer.Ordinal);
    private string? _channel;
    private DateTimeOffset? _sessionStartedAt;
    private long _messageCount;
    private int _peakViewers;
    private long _viewerSamplesSum;
    private int _viewerSamplesCount;
    private string? _lastTitle;
    private string? _lastGame;

    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;
    private bool _disposed;

    public StreamSessionStatisticsHandler(
        IChatMessenger messenger,
        IStreamStatus streamStatus,
        SettingsManager settingsManager,
        TimeProvider timeProvider,
        IEventBus eventBus,
        ActiveStreamSessionStore activeSessionStore,
        ILogger<StreamSessionStatisticsHandler> logger)
    {
        _messenger = messenger;
        _streamStatus = streamStatus;
        _settingsManager = settingsManager;
        _timeProvider = timeProvider;
        _eventBus = eventBus;
        _activeSessionStore = activeSessionStore;
        _logger = logger;

        _onlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
        _metadataResolvedSubscription = eventBus.Subscribe<StreamMetadataResolved>(this);
        _chatSubscription = eventBus.Subscribe<ChatMessageReceived>(this);
        _channelUpdatedSubscription = eventBus.Subscribe<ChannelUpdated>(this);
    }

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        var startedAt = ResolveStartedAt(@event);
        var channel = string.IsNullOrEmpty(@event.Channel)
            ? _settingsManager.Current.Twitch.Channel
            : @event.Channel;

        var initialViewers = @event.Stream?.ViewerCount ?? 0;
        var initialTitle = NullIfEmpty(@event.Stream?.Title);
        var initialGame = NullIfEmpty(@event.Stream?.GameName);

        await StopSamplingLoopAsync().ConfigureAwait(false);

        var existingDraft = _activeSessionStore.Load();
        var usableDraft = existingDraft != null && IsUsableDraft(existingDraft);

        var resume = @event.IsCatchUp
                     && usableDraft
                     && IsSameStream(existingDraft!, channel, startedAt);

        if (!resume && usableDraft)
        {
            await FinalizeDraftAsync(existingDraft!, existingDraft!.UpdatedAt, "незакрытый черновик прошлого стрима", cancellationToken)
                .ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            _channel = channel;

            if (resume)
            {
                _sessionStartedAt = existingDraft!.StartedAt;
                _messageCount = existingDraft.MessageCount;
                _peakViewers = Math.Max(existingDraft.PeakViewers, initialViewers);
                _viewerSamplesSum = existingDraft.ViewerSamplesSum;
                _viewerSamplesCount = existingDraft.ViewerSamplesCount;
                _lastTitle = initialTitle ?? existingDraft.Title;
                _lastGame = initialGame ?? existingDraft.Game;

                _chatters.Clear();

                foreach (var chatter in existingDraft.Chatters)
                {
                    _chatters[chatter.UserId] = new()
                    {
                        DisplayName = chatter.DisplayName,
                        MessageCount = chatter.MessageCount,
                    };
                }
            }
            else
            {
                _sessionStartedAt = startedAt;
                _messageCount = 0;
                _chatters.Clear();
                _peakViewers = initialViewers;
                _viewerSamplesSum = initialViewers;
                _viewerSamplesCount = initialViewers > 0 ? 1 : 0;
                _lastTitle = initialTitle;
                _lastGame = initialGame;
            }
        }

        Checkpoint();
        StartSamplingLoop();

        if (resume)
        {
            _logger.LogInformation("Сбор статистики сессии возобновлён из черновика (старт {StartedAt}, сообщений {Messages}, чаттеров {Chatters})",
                startedAt,
                existingDraft!.MessageCount,
                existingDraft.Chatters.Count);
        }
        else
        {
            _logger.LogDebug("Сбор статистики сессии запущен (старт {StartedAt}, catch-up {IsCatchUp}, начальные зрители {Viewers})",
                startedAt,
                @event.IsCatchUp,
                initialViewers);
        }
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.IsBot)
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            if (_sessionStartedAt == null)
            {
                return Task.CompletedTask;
            }

            _messageCount++;

            if (!string.IsNullOrEmpty(@event.UserId))
            {
                if (!_chatters.TryGetValue(@event.UserId, out var tally))
                {
                    tally = new();
                    _chatters[@event.UserId] = tally;
                }

                tally.MessageCount++;

                var displayName = FirstNonEmpty(@event.DisplayName, @event.Username);

                if (displayName != null)
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
            if (_sessionStartedAt == null)
            {
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(@event.Title))
            {
                _lastTitle = @event.Title;
            }

            if (!string.IsNullOrEmpty(@event.GameName))
            {
                _lastGame = @event.GameName;
            }
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(StreamMetadataResolved @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_sessionStartedAt == null)
            {
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(@event.Stream.Title))
            {
                _lastTitle = @event.Stream.Title;
            }

            if (!string.IsNullOrEmpty(@event.Stream.GameName))
            {
                _lastGame = @event.Stream.GameName;
            }

            if (@event.Stream.ViewerCount > _peakViewers)
            {
                _peakViewers = @event.Stream.ViewerCount;
            }
        }

        return Task.CompletedTask;
    }

    public async Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        if (@event.IsCatchUp)
        {
            await HandleCatchUpOfflineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StopSamplingLoopAsync().ConfigureAwait(false);

        var session = TakeMemorySession();

        if (session == null)
        {
            var orphan = _activeSessionStore.Load();

            if (orphan != null && IsUsableDraft(orphan))
            {
                await FinalizeDraftAsync(orphan, _timeProvider.GetUtcNow(), "черновик без активной сессии в памяти", cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _activeSessionStore.Delete();
            }

            _logger.LogDebug("Офлайн без активной сессии в памяти — статистика собрана из черновика (если он был)");
            return;
        }

        var endedAt = _timeProvider.GetUtcNow();
        var duration = endedAt - session.StartedAt;

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var record = new StreamSessionRecord
        {
            Channel = @event.Channel,
            StartedAt = session.StartedAt,
            EndedAt = endedAt,
            Title = session.Title,
            Game = session.Game,
            MessageCount = session.MessageCount,
            ChatterCount = session.ChatterCount,
            PeakViewers = session.PeakViewers,
            AverageViewers = session.AverageViewers,
            Chatters = session.Chatters,
        };

        await _eventBus.PublishAsync(new StreamSessionCompleted(record), cancellationToken).ConfigureAwait(false);

        _activeSessionStore.Delete();

        _logger.LogInformation("Сессия стрима канала {Channel} завершена и сохранена: длительность {Duration}, сообщений {Messages}, чаттеров {Chatters}, пик {Peak}, средний {Avg}",
            @event.Channel,
            duration,
            session.MessageCount,
            session.ChatterCount,
            session.PeakViewers,
            session.AverageViewers);

        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (!settings.StreamStatusNotificationsEnabled
            || string.IsNullOrWhiteSpace(settings.StreamEndStatsMessage))
        {
            return;
        }

        var message = MessageTemplate.For(settings.StreamEndStatsMessage)
            .With("duration", FormattingUtils.FormatTimeSpan(duration))
            .With("messages", FormattingUtils.FormatNumber(session.MessageCount))
            .With("chatters", FormattingUtils.FormatNumber(session.ChatterCount))
            .With("peakViewers", FormattingUtils.FormatNumber(session.PeakViewers))
            .With("avgViewers", FormattingUtils.FormatNumber(session.AverageViewers))
            .With("title", session.Title ?? MissingValuePlaceholder)
            .With("game", session.Game ?? MissingValuePlaceholder)
            .With("channel", @event.Channel)
            .Render();

        _logger.LogInformation("Отправка итоговой статистики стрима для канала {Channel}: длительность {Duration}, сообщений {Messages}, активных зрителей {Chatters}, пик {Peak}, средний {Avg}, игра \"{Game}\"",
            @event.Channel,
            duration,
            session.MessageCount,
            session.ChatterCount,
            session.PeakViewers,
            session.AverageViewers,
            session.Game);

        _messenger.Send(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _onlineSubscription.Dispose();
        _offlineSubscription.Dispose();
        _metadataResolvedSubscription.Dispose();
        _chatSubscription.Dispose();
        _channelUpdatedSubscription.Dispose();

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

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        return NullIfEmpty(primary) ?? NullIfEmpty(fallback);
    }

    private static bool IsUsableDraft(ActiveStreamSession draft)
    {
        return draft.StartedAt != default && !string.IsNullOrEmpty(draft.Channel);
    }

    private static bool IsSameStream(ActiveStreamSession draft, string channel, DateTimeOffset startedAt)
    {
        return string.Equals(draft.Channel, channel, StringComparison.OrdinalIgnoreCase)
               && (draft.StartedAt - startedAt).Duration() <= StreamMatchTolerance;
    }

    private static List<StreamSessionChatter> SortChatters(IEnumerable<StreamSessionChatter> chatters)
    {
        return chatters
            .OrderByDescending(chatter => chatter.MessageCount)
            .ThenBy(chatter => chatter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static StreamSessionRecord ToRecord(ActiveStreamSession draft, DateTimeOffset endedAt)
    {
        var averageViewers = draft.ViewerSamplesCount > 0
            ? (int)Math.Round((double)draft.ViewerSamplesSum / draft.ViewerSamplesCount, MidpointRounding.AwayFromZero)
            : 0;

        var chatters = SortChatters(draft.Chatters.Select(chatter => new StreamSessionChatter
        {
            UserId = chatter.UserId,
            DisplayName = string.IsNullOrEmpty(chatter.DisplayName) ? chatter.UserId : chatter.DisplayName,
            MessageCount = chatter.MessageCount,
        }));

        return new()
        {
            Channel = draft.Channel,
            StartedAt = draft.StartedAt,
            EndedAt = endedAt < draft.StartedAt ? draft.StartedAt : endedAt,
            Title = draft.Title,
            Game = draft.Game,
            MessageCount = draft.MessageCount,
            ChatterCount = draft.Chatters.Count,
            PeakViewers = draft.PeakViewers,
            AverageViewers = averageViewers,
            Chatters = chatters,
        };
    }

    private DateTimeOffset ResolveStartedAt(StreamWentOnline @event)
    {
        return @event.Stream?.StartedAt is { } streamStart && streamStart != default
            ? new(DateTime.SpecifyKind(streamStart, DateTimeKind.Utc))
            : _timeProvider.GetUtcNow();
    }

    private Task HandleCatchUpOfflineAsync(CancellationToken cancellationToken)
    {
        bool hasMemorySession;

        lock (_stateLock)
        {
            hasMemorySession = _sessionStartedAt != null;
        }

        if (hasMemorySession)
        {
            _logger.LogDebug("Catch-up offline при активной сессии в памяти — событие проигнорировано");
            return Task.CompletedTask;
        }

        var orphan = _activeSessionStore.Load();

        if (orphan != null && IsUsableDraft(orphan))
        {
            return FinalizeDraftAsync(orphan, orphan.UpdatedAt, "стрим завершился во время простоя бота", cancellationToken);
        }

        _activeSessionStore.Delete();

        return Task.CompletedTask;
    }

    private async Task FinalizeDraftAsync(ActiveStreamSession draft, DateTimeOffset endedAt, string reason, CancellationToken cancellationToken)
    {
        var record = ToRecord(draft, endedAt);

        await _eventBus.PublishAsync(new StreamSessionCompleted(record), cancellationToken).ConfigureAwait(false);
        _activeSessionStore.Delete();

        _logger.LogInformation("Восстановлена сессия стрима из черновика ({Reason}): канал {Channel}, сообщений {Messages}, чаттеров {Chatters}, длительность {Duration}",
            reason,
            record.Channel,
            record.MessageCount,
            record.ChatterCount,
            record.Duration);
    }

    private MemorySession? TakeMemorySession()
    {
        lock (_stateLock)
        {
            if (_sessionStartedAt == null)
            {
                return null;
            }

            var averageViewers = _viewerSamplesCount > 0
                ? (int)Math.Round((double)_viewerSamplesSum / _viewerSamplesCount, MidpointRounding.AwayFromZero)
                : 0;

            var chatters = SortChatters(_chatters.Select(pair => new StreamSessionChatter
            {
                UserId = pair.Key,
                DisplayName = string.IsNullOrEmpty(pair.Value.DisplayName) ? pair.Key : pair.Value.DisplayName,
                MessageCount = pair.Value.MessageCount,
            }));

            var result = new MemorySession(_sessionStartedAt.Value,
                _messageCount,
                _chatters.Count,
                _peakViewers,
                averageViewers,
                _lastTitle,
                _lastGame,
                chatters);

            _channel = null;
            _sessionStartedAt = null;
            _messageCount = 0;
            _chatters.Clear();
            _peakViewers = 0;
            _viewerSamplesSum = 0;
            _viewerSamplesCount = 0;
            _lastTitle = null;
            _lastGame = null;

            return result;
        }
    }

    private ActiveStreamSession? CreateCheckpointSnapshot()
    {
        lock (_stateLock)
        {
            if (_sessionStartedAt == null || _channel == null)
            {
                return null;
            }

            return new()
            {
                Channel = _channel,
                StartedAt = _sessionStartedAt.Value,
                UpdatedAt = _timeProvider.GetUtcNow(),
                MessageCount = _messageCount,
                PeakViewers = _peakViewers,
                ViewerSamplesSum = _viewerSamplesSum,
                ViewerSamplesCount = _viewerSamplesCount,
                Title = _lastTitle,
                Game = _lastGame,
                Chatters = _chatters
                    .Select(pair => new ActiveStreamSessionChatter
                    {
                        UserId = pair.Key,
                        DisplayName = pair.Value.DisplayName,
                        MessageCount = pair.Value.MessageCount,
                    })
                    .ToList(),
            };
        }
    }

    private void Checkpoint()
    {
        var snapshot = CreateCheckpointSnapshot();

        if (snapshot == null)
        {
            return;
        }

        try
        {
            _activeSessionStore.Save(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось сохранить чекпойнт активной сессии стрима");
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
            _samplingCts?.Dispose();
            _samplingCts = null;
            _samplingTask = null;
        }

        if (cts != null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }
        }

        if (task != null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on stop
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
        using var timer = new PeriodicTimer(ViewerSampleInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                SampleStreamSnapshot();
                Checkpoint();
            }
        }
        catch (OperationCanceledException)
        {
            // expected when sampling loop is cancelled
        }
    }

    private void SampleStreamSnapshot()
    {
        var snapshot = _streamStatus.CurrentStream;

        if (snapshot == null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_sessionStartedAt == null)
            {
                return;
            }

            if (snapshot.ViewerCount > _peakViewers)
            {
                _peakViewers = snapshot.ViewerCount;
            }

            _viewerSamplesSum += snapshot.ViewerCount;
            _viewerSamplesCount++;

            if (!string.IsNullOrEmpty(snapshot.Title))
            {
                _lastTitle = snapshot.Title;
            }

            if (!string.IsNullOrEmpty(snapshot.GameName))
            {
                _lastGame = snapshot.GameName;
            }
        }
    }

    private sealed record MemorySession(
        DateTimeOffset StartedAt,
        long MessageCount,
        int ChatterCount,
        int PeakViewers,
        int AverageViewers,
        string? Title,
        string? Game,
        List<StreamSessionChatter> Chatters);

    private sealed class ChatterTally
    {
        public string DisplayName { get; set; } = string.Empty;
        public long MessageCount { get; set; }
    }
}
