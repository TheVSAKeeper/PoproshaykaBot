using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Streaming;

public class StreamStatusManager : IStreamStatus, IStreamHostedComponent, IAsyncDisposable
{
    internal static readonly TimeSpan StuckOnlineThreshold = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly ITwitchHelixClient _helix;
    private readonly IBroadcasterIdProvider _broadcasterIdProvider;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StreamStatusManager> _logger;

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _stateLock = new();

    private IDisposable? _channelUpdatedSubscription;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _metadataRetryCts;
    private DateTime? _firstOfflineFromApiUtc;
    private ChannelUpdated? _lastChannelUpdate;
    private StreamStatus _currentStatus = StreamStatus.Unknown;
    private StreamInfo? _currentStream;
    private bool _subscribed;
    private bool _disposed;

    public StreamStatusManager(
        ITwitchEventSubClient eventSubClient,
        [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
        ITwitchHelixClient helix,
        IBroadcasterIdProvider broadcasterIdProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<StreamStatusManager> logger)
    {
        _eventSubClient = eventSubClient;
        _helix = helix;
        _broadcasterIdProvider = broadcasterIdProvider;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public StreamStatus CurrentStatus
    {
        get
        {
            lock (_stateLock)
            {
                return _currentStatus;
            }
        }
    }

    public StreamInfo? CurrentStream
    {
        get
        {
            lock (_stateLock)
            {
                return _currentStream;
            }
        }
    }

    public string Name => "Отслеживание статуса стрима";

    public int StartOrder => 256;

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            _logger.LogDebug("StreamStatusManager.StartAsync проигнорирован: подписки уже установлены");
            return Task.CompletedTask;
        }

        _runCts = new();

        _eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
        _eventSubClient.OnNotification += OnNotificationAsync;
        _eventSubClient.OnSessionReconnect += OnSessionReconnectAsync;
        _eventSubClient.OnRevocation += OnRevocationAsync;
        _eventSubClient.OnDisconnected += OnDisconnectedAsync;

        _channelUpdatedSubscription = _eventBus.Subscribe<ChannelUpdated>(OnChannelUpdated);
        _subscribed = true;

        _logger.LogInformation("StreamStatusManager: подписки на EventSub установлены");
        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            _logger.LogDebug("StreamStatusManager.StopAsync проигнорирован: подписки уже сняты");
            return;
        }

        _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
        _eventSubClient.OnNotification -= OnNotificationAsync;
        _eventSubClient.OnSessionReconnect -= OnSessionReconnectAsync;
        _eventSubClient.OnRevocation -= OnRevocationAsync;
        _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

        _channelUpdatedSubscription?.Dispose();
        _channelUpdatedSubscription = null;
        _subscribed = false;

        if (_runCts != null)
        {
            await _runCts.CancelAsync().ConfigureAwait(false);
        }

        var pending = MetadataRetryTask;

        if (pending != null)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry-цикл метаданных стрима завершился с ошибкой при остановке");
            }
        }

        _runCts?.Dispose();
        _runCts = null;

        Interlocked.Exchange(ref _metadataRetryCts, null);

        SetState(StreamStatus.Unknown, null);

        lock (_stateLock)
        {
            _firstOfflineFromApiUtc = null;
            _lastChannelUpdate = null;
        }

        _logger.LogInformation("StreamStatusManager: подписки на EventSub сняты, состояние сброшено в Unknown");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Уничтожение ресурсов StreamStatusManager");
        _disposed = true;

        await _disposeCts.CancelAsync();

        if (_runCts != null)
        {
            await _runCts.CancelAsync();
        }

        var pending = MetadataRetryTask;

        if (pending != null)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _disposeCts.Dispose();
        _runCts?.Dispose();
        _runCts = null;

        Interlocked.Exchange(ref _metadataRetryCts, null);

        if (_subscribed)
        {
            _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
            _eventSubClient.OnNotification -= OnNotificationAsync;
            _eventSubClient.OnSessionReconnect -= OnSessionReconnectAsync;
            _eventSubClient.OnRevocation -= OnRevocationAsync;
            _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

            _channelUpdatedSubscription?.Dispose();
            _channelUpdatedSubscription = null;
            _subscribed = false;
        }

        GC.SuppressFinalize(this);
    }

    public async Task RefreshLiveSnapshotAsync()
    {
        var token = _disposeCts.Token;

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(token);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogDebug("Live-snapshot пропущен: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Live-snapshot: запрос статуса для BroadcasterId {BroadcasterId} (текущий статус {CurrentStatus})", broadcasterId, CurrentStatus);
            var stream = await _helix.GetStreamAsync(broadcasterId, token);
            var apiSaysOnline = stream != null;

            if (apiSaysOnline)
            {
                bool transitioned;
                StreamStatus previousStatus;
                lock (_stateLock)
                {
                    _firstOfflineFromApiUtc = null;
                    previousStatus = _currentStatus;
                    transitioned = previousStatus != StreamStatus.Online;
                    _currentStatus = StreamStatus.Online;
                    _currentStream = ApplyChannelUpdateOverlayLocked(MapToStreamInfo(stream!));
                }

                if (transitioned)
                {
                    _logger.LogInformation("Live-snapshot: переход {OldStatus} → Online для BroadcasterId {BroadcasterId}", previousStatus, broadcasterId);
                    await PublishStatusTransitionAsync(StreamStatus.Online, true).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Live-snapshot: статус Online подтверждён для BroadcasterId {BroadcasterId}", broadcasterId);
                }

                return;
            }

            StreamStatus statusForOfflineBranch;
            lock (_stateLock)
            {
                statusForOfflineBranch = _currentStatus;
            }

            if (statusForOfflineBranch == StreamStatus.Online)
            {
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                bool forceOffline;
                TimeSpan elapsed;
                lock (_stateLock)
                {
                    _firstOfflineFromApiUtc ??= nowUtc;
                    elapsed = nowUtc - _firstOfflineFromApiUtc.Value;
                    forceOffline = elapsed >= StuckOnlineThreshold;

                    if (forceOffline)
                    {
                        _currentStatus = StreamStatus.Offline;
                        _currentStream = null;
                        _firstOfflineFromApiUtc = null;
                    }
                }

                if (forceOffline)
                {
                    _logger.LogWarning("Принудительный перевод в Offline после {ElapsedSeconds} с расхождения с API для BroadcasterId {BroadcasterId}",
                        (int)elapsed.TotalSeconds, broadcasterId);

                    await PublishStatusTransitionAsync(StreamStatus.Offline).ConfigureAwait(false);
                    return;
                }

                _logger.LogWarning("API сообщает офлайн при локальном Online, ждём подтверждения (расхождение {Elapsed}/{Threshold})", elapsed, StuckOnlineThreshold);
                return;
            }

            lock (_stateLock)
            {
                _firstOfflineFromApiUtc = null;
                _currentStream = null;
            }

            _logger.LogDebug("Live-snapshot: статус Offline подтверждён для BroadcasterId {BroadcasterId}", broadcasterId);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            _logger.LogDebug("Live-snapshot отменён: StreamStatusManager уничтожается");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить live-snapshot стрима: {Reason}", SafeMessage(ex));
        }
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub WebSocket подключен. SessionId: {SessionId}", args.SessionId);

        lock (_stateLock)
        {
            _lastChannelUpdate = null;
        }

        await CreateEventSubSubscriptionsAsync(args.SessionId, cancellationToken);
        await InitializeFromApiAsync(cancellationToken);
    }

    private async Task OnNotificationAsync(EventSubNotificationArgs args, CancellationToken cancellationToken)
    {
        switch (args.SubscriptionType)
        {
            case "stream.online":
                await HandleStreamOnlineAsync(args.Payload, cancellationToken);
                break;

            case "stream.offline":
                await HandleStreamOfflineAsync(cancellationToken);
                break;

            default:
                _logger.LogDebug("StreamStatusManager игнорирует EventSub-нотификацию типа {Type}", args.SubscriptionType);
                break;
        }
    }

    private Task OnSessionReconnectAsync(EventSubReconnectArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub session_reconnect → {Url} (старый session: {OldSession})", args.ReconnectUrl, args.OldSessionId);
        return Task.CompletedTask;
    }

    private async Task OnRevocationAsync(EventSubRevocationArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub revocation: id={Id}, type={Type}, status={Status}", args.SubscriptionId, args.SubscriptionType, args.Status);

        if (string.IsNullOrEmpty(_eventSubClient.SessionId))
        {
            _logger.LogDebug("Восстановление подписки {Type} пропущено: SessionId уже отсутствует", args.SubscriptionType);
            return;
        }

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogWarning("Восстановление подписки {Type} прервано: BroadcasterId недоступен", args.SubscriptionType);
                return;
            }

            var condition = new Dictionary<string, string>
            {
                { "broadcaster_user_id", broadcasterId },
            };

            var newId = await _helix.CreateEventSubSubscriptionAsync(args.SubscriptionType,
                "1",
                condition,
                _eventSubClient.SessionId,
                cancellationToken);

            _logger.LogInformation("Подписка {Type} восстановлена после revocation. Новый id: {Id}", args.SubscriptionType, newId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Восстановление подписки {Type} отменено", args.SubscriptionType);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить подписку {Type} после revocation: {Reason}", args.SubscriptionType, SafeMessage(ex));
        }
    }

    private Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub WebSocket отключен ({Reason}) — статус стрима сброшен в Unknown до восстановления соединения", args.Reason);

        SetState(StreamStatus.Unknown, null);

        lock (_stateLock)
        {
            _firstOfflineFromApiUtc = null;
            _lastChannelUpdate = null;
        }

        return Task.CompletedTask;
    }

    private static StreamInfo MapToStreamInfo(HelixStreamInfo stream)
    {
        return new()
        {
            Id = stream.Id,
            UserId = stream.UserId,
            UserLogin = stream.UserLogin,
            UserName = stream.UserName,
            GameId = stream.GameId,
            GameName = stream.GameName,
            Title = stream.Title,
            Language = stream.Language,
            ViewerCount = stream.ViewerCount,
            StartedAt = stream.StartedAt,
            ThumbnailUrl = stream.ThumbnailUrl,
            Tags = stream.Tags,
            IsMature = stream.IsMature,
        };
    }

    private static StreamInfo CloneStreamInfo(StreamInfo source)
    {
        return new()
        {
            Id = source.Id,
            UserId = source.UserId,
            UserLogin = source.UserLogin,
            UserName = source.UserName,
            GameId = source.GameId,
            GameName = source.GameName,
            Title = source.Title,
            Language = source.Language,
            ViewerCount = source.ViewerCount,
            StartedAt = source.StartedAt,
            ThumbnailUrl = source.ThumbnailUrl,
            Tags = source.Tags,
            IsMature = source.IsMature,
        };
    }

    private static string SafeMessage(Exception exception)
    {
        if (exception is HelixRequestException helixEx)
        {
            return helixEx.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => "недостаточно прав Twitch, нужна повторная авторизация",
                HttpStatusCode.NotFound
                    => "ресурс Twitch не найден",
                HttpStatusCode.TooManyRequests
                    => "слишком много запросов, попробуем позже",
                _ => "Twitch отклонил запрос",
            };
        }

        return exception switch
        {
            OperationCanceledException => "операция отменена",
            TimeoutException => "превышено время ожидания ответа Twitch",
            WebSocketException or SocketException => "соединение с EventSub прервано",
            HttpRequestException => "сетевая ошибка при обращении к Twitch",
            UnauthorizedAccessException => "ошибка авторизации в Twitch",
            JsonException => "некорректный ответ Twitch",
            _ => "внутренняя ошибка",
        };
    }

    private async Task InitializeFromApiAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var token = linkedCts.Token;

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(token);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogDebug("Инициализация по API пропущена: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Запрос начального статуса стрима через API для BroadcasterId: {BroadcasterId}", broadcasterId);
            var stream = await _helix.GetStreamAsync(broadcasterId, token);

            var isOnline = stream != null;
            var newStatus = isOnline ? StreamStatus.Online : StreamStatus.Offline;
            var newStream = isOnline ? ApplyChannelUpdateOverlay(MapToStreamInfo(stream!)) : null;

            bool transitioned;
            StreamStatus previousStatus;
            lock (_stateLock)
            {
                _firstOfflineFromApiUtc = null;
                previousStatus = _currentStatus;
                transitioned = previousStatus != newStatus;
                _currentStatus = newStatus;
                _currentStream = newStream;
            }

            if (transitioned)
            {
                _logger.LogInformation("Инициализация: статус стрима {OldStatus} → {NewStatus} для BroadcasterId {BroadcasterId}", previousStatus, newStatus, broadcasterId);
                await PublishStatusTransitionAsync(newStatus, isOnline).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("Инициализация: статус стрима {Status} подтверждён для BroadcasterId {BroadcasterId}", newStatus, broadcasterId);
            }

            _logger.LogInformation(isOnline
                ? "Начальный статус: онлайн (по данным API)"
                : "Начальный статус: офлайн (по данным API)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("Инициализация по API отменена (cancellationToken: {CallerCancelled}, dispose: {DisposeCancelled})",
                cancellationToken.IsCancellationRequested, _disposeCts.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось получить начальный статус стрима: {Reason}", SafeMessage(ex));
        }
    }

    private async Task<bool> FetchAndUpdateMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return false;
            }

            var stream = await _helix.GetStreamAsync(broadcasterId, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            var info = ApplyChannelUpdateOverlay(MapToStreamInfo(stream));
            SetStream(info);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения метаданных стрима: {Reason}", SafeMessage(ex));
            return false;
        }
    }

    private void OnChannelUpdated(ChannelUpdated @event)
    {
        StreamInfo? updatedStream = null;

        lock (_stateLock)
        {
            _lastChannelUpdate = @event;

            if (_currentStream != null)
            {
                updatedStream = CloneStreamInfo(_currentStream);
                updatedStream.Title = @event.Title;
                updatedStream.Language = @event.Language;
                updatedStream.GameId = @event.GameId;
                updatedStream.GameName = @event.GameName;
                _currentStream = updatedStream;
            }
        }

        if (updatedStream == null)
        {
            _logger.LogDebug("ChannelUpdated сохранён без обновления CurrentStream: стрим не в состоянии online");
            return;
        }

        _logger.LogInformation("Метаданные стрима обновлены через channel.update: title={Title}, game={GameName}",
            @event.Title, @event.GameName);
    }

    private StreamInfo ApplyChannelUpdateOverlay(StreamInfo info)
    {
        lock (_stateLock)
        {
            return ApplyChannelUpdateOverlayLocked(info);
        }
    }

    private StreamInfo ApplyChannelUpdateOverlayLocked(StreamInfo info)
    {
        var update = _lastChannelUpdate;

        if (update is null)
        {
            return info;
        }

        info.Title = update.Title;
        info.Language = update.Language;
        info.GameId = update.GameId;
        info.GameName = update.GameName;
        return info;
    }

    private void SetState(StreamStatus status, StreamInfo? stream)
    {
        lock (_stateLock)
        {
            _currentStatus = status;
            _currentStream = stream;
        }
    }

    private void SetStream(StreamInfo? stream)
    {
        lock (_stateLock)
        {
            _currentStream = stream;
        }
    }

    private async Task HandleStreamOnlineAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var data = payload.Deserialize<EventSubStreamOnlinePayloadDto>(WebJsonOptions);

        _logger.LogInformation("Стрим запущен (EventSub), тип: {EventType}", data?.Event?.Type ?? "live");

        bool transitioned;
        StreamStatus previousStatus;
        lock (_stateLock)
        {
            previousStatus = _currentStatus;
            transitioned = previousStatus != StreamStatus.Online;
            _currentStatus = StreamStatus.Online;
        }

        if (transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Online", previousStatus);
            await PublishStatusTransitionAsync(StreamStatus.Online).ConfigureAwait(false);
        }

        await CancelPreviousMetadataRetryAsync().ConfigureAwait(false);

        var runToken = _runCts?.Token ?? CancellationToken.None;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, runToken);
        _metadataRetryCts = linkedCts;
        var loopToken = linkedCts.Token;

        MetadataRetryTask = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Запуск опроса метаданных стрима (попыток до 6, бэкофф 5/10/15/20/25/30 сек)");

                for (var i = 0; i < 6; i++)
                {
                    loopToken.ThrowIfCancellationRequested();

                    var metadataLoaded = await FetchAndUpdateMetadataAsync(loopToken);

                    if (metadataLoaded && CurrentStream != null)
                    {
                        _logger.LogInformation("Метаданные стрима получены из API (попытка {Attempt}/6)", i + 1);
                        await PublishMetadataResolvedAsync().ConfigureAwait(false);
                        break;
                    }

                    if (CurrentStatus != StreamStatus.Online)
                    {
                        _logger.LogDebug("Опрос метаданных прерван: стрим больше не онлайн (попытка {Attempt}/6)", i + 1);
                        break;
                    }

                    var delaySeconds = 5 * (i + 1);
                    _logger.LogWarning("Метаданные ещё не доступны в API. Повтор через {DelaySeconds} сек (попытка {Attempt}/6)", delaySeconds, i + 1);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), loopToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Опрос метаданных стрима отменён (новый stream.online или остановка менеджера)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Непредвиденная ошибка во время фонового опроса метаданных стрима");
            }
            finally
            {
                Interlocked.CompareExchange(ref _metadataRetryCts, null, linkedCts);
                linkedCts.Dispose();
            }
        }, loopToken);
    }

    private async Task CancelPreviousMetadataRetryAsync()
    {
        var previousCts = Interlocked.Exchange(ref _metadataRetryCts, null);
        var previousTask = MetadataRetryTask;

        if (previousCts != null)
        {
            try
            {
                await previousCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (previousTask is { IsCompleted: false })
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Предыдущий retry-цикл метаданных завершился с ошибкой при отмене (игнорируется)");
            }
        }
    }

    private async Task HandleStreamOfflineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Стрим завершен (EventSub)");

        bool transitioned;
        StreamStatus previousStatus;
        lock (_stateLock)
        {
            previousStatus = _currentStatus;
            transitioned = previousStatus != StreamStatus.Offline;
            _currentStatus = StreamStatus.Offline;
            _currentStream = null;
        }

        if (transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Offline", previousStatus);
            await PublishStatusTransitionAsync(StreamStatus.Offline).ConfigureAwait(false);
        }
    }

    private Task PublishStatusTransitionAsync(StreamStatus newStatus, bool isCatchUp = false)
    {
        var channel = _settingsManager.Current.Twitch.Channel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            _logger.LogDebug("Публикация события статуса стрима пропущена: имя канала неизвестно");
            return Task.CompletedTask;
        }

        return newStatus switch
        {
            StreamStatus.Online => _eventBus.PublishAsync(new StreamWentOnline(channel, CurrentStream, isCatchUp)),
            StreamStatus.Offline => _eventBus.PublishAsync(new StreamWentOffline(channel)),
            _ => Task.CompletedTask,
        };
    }

    private Task PublishMetadataResolvedAsync()
    {
        var channel = _settingsManager.Current.Twitch.Channel;
        var stream = CurrentStream;

        if (string.IsNullOrWhiteSpace(channel) || stream == null)
        {
            _logger.LogDebug("Публикация StreamMetadataResolved пропущена (channel пуст или stream null)");
            return Task.CompletedTask;
        }

        return _eventBus.PublishAsync(new StreamMetadataResolved(channel, stream));
    }

    private async Task CreateEventSubSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            _logger.LogError("Невозможно создать подписки EventSub: BroadcasterId недоступен");
            return;
        }

        _logger.LogInformation("Создание подписок EventSub для BroadcasterId: {BroadcasterId}", broadcasterId);

        var subscriptionsCreated = 0;
        var condition = new Dictionary<string, string>
        {
            { "broadcaster_user_id", broadcasterId },
        };

        try
        {
            var subscriptionId = await _helix.CreateEventSubSubscriptionAsync("stream.online",
                "1",
                condition,
                sessionId,
                cancellationToken);

            subscriptionsCreated++;

            _logger.LogInformation("Подписка 'stream.online' создана. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}", subscriptionId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка создания подписки stream.online для BroadcasterId {BroadcasterId}: {Reason}", broadcasterId, SafeMessage(ex));
        }

        try
        {
            var subscriptionId = await _helix.CreateEventSubSubscriptionAsync("stream.offline",
                "1",
                condition,
                sessionId,
                cancellationToken);

            subscriptionsCreated++;

            _logger.LogInformation("Подписка 'stream.offline' создана. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}", subscriptionId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка создания подписки stream.offline для BroadcasterId {BroadcasterId}: {Reason}", broadcasterId, SafeMessage(ex));
        }

        if (subscriptionsCreated == 2)
        {
            _logger.LogInformation("Все необходимые подписки EventSub успешно созданы для BroadcasterId: {BroadcasterId}", broadcasterId);
        }
        else
        {
            _logger.LogWarning("Создано только {CreatedCount}/2 подписок EventSub stream.* для BroadcasterId {BroadcasterId} — статус стрима будет неполным",
                subscriptionsCreated, broadcasterId);
        }
    }

    internal Task? MetadataRetryTask { get; private set; }
}
