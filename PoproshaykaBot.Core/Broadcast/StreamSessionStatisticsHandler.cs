using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings;
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

    private readonly IChatMessenger _messenger;
    private readonly IStreamStatus _streamStatus;
    private readonly SettingsManager _settingsManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StreamSessionStatisticsHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly IDisposable _metadataResolvedSubscription;
    private readonly IDisposable _chatSubscription;
    private readonly IDisposable _channelUpdatedSubscription;

    private readonly object _stateLock = new();
    private readonly HashSet<string> _chatters = new(StringComparer.Ordinal);
    private DateTimeOffset? _sessionStartedAt;
    private long _messageCount;
    private int _peakViewers;
    private long _viewerSamplesSum;
    private int _viewerSamplesCount;
    private string? _lastTitle;
    private string? _lastGame;

    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;

    public StreamSessionStatisticsHandler(
        IChatMessenger messenger,
        IStreamStatus streamStatus,
        SettingsManager settingsManager,
        TimeProvider timeProvider,
        IEventBus eventBus,
        ILogger<StreamSessionStatisticsHandler> logger)
    {
        _messenger = messenger;
        _streamStatus = streamStatus;
        _settingsManager = settingsManager;
        _timeProvider = timeProvider;
        _logger = logger;

        _onlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
        _metadataResolvedSubscription = eventBus.Subscribe<StreamMetadataResolved>(this);
        _chatSubscription = eventBus.Subscribe<ChatMessageReceived>(this);
        _channelUpdatedSubscription = eventBus.Subscribe<ChannelUpdated>(this);
    }

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        var startedAt = @event.Stream?.StartedAt is { } streamStart && streamStart != default
            ? new(DateTime.SpecifyKind(streamStart, DateTimeKind.Utc))
            : _timeProvider.GetUtcNow();

        var initialViewers = @event.Stream?.ViewerCount ?? 0;
        var initialTitle = NullIfEmpty(@event.Stream?.Title);
        var initialGame = NullIfEmpty(@event.Stream?.GameName);

        await StopSamplingLoopAsync().ConfigureAwait(false);

        lock (_stateLock)
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

        StartSamplingLoop();

        _logger.LogDebug("Сбор статистики сессии запущен (старт {StartedAt}, catch-up {IsCatchUp}, начальные зрители {Viewers})",
            startedAt,
            @event.IsCatchUp,
            initialViewers);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_sessionStartedAt == null)
            {
                return Task.CompletedTask;
            }

            _messageCount++;

            if (!string.IsNullOrEmpty(@event.UserId))
            {
                _chatters.Add(@event.UserId);
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
        await StopSamplingLoopAsync().ConfigureAwait(false);

        DateTimeOffset? sessionStartedAt;
        long messageCount;
        int chatterCount;
        int peakViewers;
        int avgViewers;
        string? lastTitle;
        string? lastGame;

        lock (_stateLock)
        {
            sessionStartedAt = _sessionStartedAt;
            messageCount = _messageCount;
            chatterCount = _chatters.Count;
            peakViewers = _peakViewers;
            avgViewers = _viewerSamplesCount > 0
                ? (int)Math.Round((double)_viewerSamplesSum / _viewerSamplesCount, MidpointRounding.AwayFromZero)
                : 0;

            lastTitle = _lastTitle;
            lastGame = _lastGame;

            _sessionStartedAt = null;
            _messageCount = 0;
            _chatters.Clear();
            _peakViewers = 0;
            _viewerSamplesSum = 0;
            _viewerSamplesCount = 0;
            _lastTitle = null;
            _lastGame = null;
        }

        if (sessionStartedAt == null)
        {
            _logger.LogDebug("Офлайн без активной сессии — статистика не отправляется");
            return;
        }

        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (!settings.StreamStatusNotificationsEnabled
            || string.IsNullOrWhiteSpace(settings.StreamEndStatsMessage))
        {
            return;
        }

        var duration = _timeProvider.GetUtcNow() - sessionStartedAt.Value;

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var message = MessageTemplate.For(settings.StreamEndStatsMessage)
            .With("duration", FormattingUtils.FormatTimeSpan(duration))
            .With("messages", FormattingUtils.FormatNumber(messageCount))
            .With("chatters", FormattingUtils.FormatNumber(chatterCount))
            .With("peakViewers", FormattingUtils.FormatNumber(peakViewers))
            .With("avgViewers", FormattingUtils.FormatNumber(avgViewers))
            .With("title", lastTitle ?? MissingValuePlaceholder)
            .With("game", lastGame ?? MissingValuePlaceholder)
            .With("channel", @event.Channel)
            .Render();

        _logger.LogInformation("Отправка итоговой статистики стрима для канала {Channel}: длительность {Duration}, сообщений {Messages}, активных зрителей {Chatters}, пик {Peak}, средний {Avg}, игра \"{Game}\"",
            @event.Channel,
            duration,
            messageCount,
            chatterCount,
            peakViewers,
            avgViewers,
            lastGame);

        _messenger.Send(message);
    }

    public async ValueTask DisposeAsync()
    {
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
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private void StartSamplingLoop()
    {
        var newCts = new CancellationTokenSource();
        var token = newCts.Token;
        var newTask = Task.Run(() => RunSamplingLoopAsync(token), token);

        lock (_stateLock)
        {
            _samplingCts = newCts;
            _samplingTask = newTask;
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

        if (cts != null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
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
            }
        }
        catch (OperationCanceledException)
        {
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
}
