using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Reconnection;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Streaming;

public class StreamStatusManager : IAsyncDisposable
{
    private const int MaxReconnectAttempts = 5;
    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly ITwitchHelixClient _helix;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StreamStatusManager> _logger;
    private readonly ExponentialBackoffPolicy _reconnectionPolicy = new(MaxReconnectAttempts);

    private readonly object _lockObj = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private string? _broadcasterUserId;
    private string? _channelName;
    private bool _disposed;
    private bool _isMonitoring;
    private bool _stopRequested;
    private CancellationTokenSource? _reconnectCts;

    public StreamStatusManager(
        ITwitchEventSubClient eventSubClient,
        ITwitchHelixClient helix,
        IEventBus eventBus,
        ILogger<StreamStatusManager> logger)
    {
        _eventSubClient = eventSubClient;
        _helix = helix;
        _eventBus = eventBus;
        _logger = logger;

        _eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
        _eventSubClient.OnNotification += OnNotificationAsync;
        _eventSubClient.OnSessionReconnect += OnSessionReconnectAsync;
        _eventSubClient.OnRevocation += OnRevocationAsync;
        _eventSubClient.OnDisconnected += OnDisconnectedAsync;
    }

    public StreamStatus CurrentStatus { get; private set; } = StreamStatus.Unknown;
    public StreamInfo? CurrentStream { get; private set; }

    public async Task StartMonitoringAsync(string channelName)
    {
        _logger.LogDebug("Попытка запуска мониторинга для канала: {ChannelName}", channelName);

        lock (_lockObj)
        {
            if (_isMonitoring)
            {
                _logger.LogWarning("Мониторинг для канала {ChannelName} уже запущен или находится в процессе запуска", channelName);
                return;
            }

            _isMonitoring = true;
            _stopRequested = false;
        }

        if (string.IsNullOrWhiteSpace(channelName))
        {
            _logger.LogWarning("Не удалось запустить мониторинг: имя канала пустое");
            PublishMonitoringError("Имя канала не может быть пустым");
            SetMonitoringStopped();
            return;
        }

        var userId = await GetBroadcasterUserIdAsync(channelName);

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Не удалось запустить мониторинг: не удалось получить ID пользователя для канала {ChannelName}", channelName);
            SetMonitoringStopped();
            return;
        }

        _broadcasterUserId = userId;
        _channelName = channelName;
        _reconnectionPolicy.Reset();

        try
        {
            await RefreshCurrentStatusAsync();

            PublishMonitoringLog("Подключение к EventSub WebSocket...");
            _logger.LogDebug("Подключение к EventSub WebSocket для BroadcasterId: {BroadcasterId}", _broadcasterUserId);

            await _eventSubClient.StartAsync(_disposeCts.Token);

            _logger.LogInformation("Соединение EventSub инициировано для BroadcasterId: {BroadcasterId}. Ожидание подтверждения...", _broadcasterUserId);
            PublishMonitoringLog("Ожидание подтверждения подключения...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Исключение при запуске мониторинга для канала {ChannelName}", channelName);
            PublishMonitoringError($"Ошибка запуска мониторинга: {SafeMessage(ex)}");
            SetMonitoringStopped();
            throw;
        }
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            _logger.LogDebug("Вызван StopMonitoringAsync, но объект уже уничтожен (Disposed)");
            return;
        }

        _logger.LogDebug("Остановка мониторинга для BroadcasterId: {BroadcasterId}", _broadcasterUserId);

        try
        {
            lock (_lockObj)
            {
                _stopRequested = true;
                CancelAndResetReconnectToken();
            }

            PublishMonitoringLog("Отключение от EventSub WebSocket...");

            await _eventSubClient.StopAsync(cancellationToken);

            CurrentStatus = StreamStatus.Unknown;
            SetMonitoringStopped();

            _logger.LogInformation("Мониторинг стрима успешно остановлен для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringLog("Мониторинг стрима остановлен");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при остановке мониторинга для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringError($"Ошибка остановки мониторинга: {SafeMessage(ex)}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Уничтожение ресурсов StreamStatusManager");
        _disposed = true;

        lock (_lockObj)
        {
            _stopRequested = true;
            CancelAndResetReconnectToken();
        }

        await _disposeCts.CancelAsync();
        _disposeCts.Dispose();

        await StopMonitoringAsync();

        _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
        _eventSubClient.OnNotification -= OnNotificationAsync;
        _eventSubClient.OnSessionReconnect -= OnSessionReconnectAsync;
        _eventSubClient.OnRevocation -= OnRevocationAsync;
        _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

        GC.SuppressFinalize(this);
    }

    public async Task RefreshCurrentStatusAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_broadcasterUserId))
            {
                _logger.LogDebug("Обновление статуса пропущено: BroadcasterUserId пуст");
                return;
            }

            _logger.LogDebug("Запрос текущего статуса стрима через API для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            var stream = await _helix.GetStreamAsync(_broadcasterUserId);

            var isOnline = stream != null;
            var newStatus = isOnline ? StreamStatus.Online : StreamStatus.Offline;

            if (CurrentStatus == StreamStatus.Online && newStatus == StreamStatus.Offline)
            {
                _logger.LogWarning("Обнаружена задержка Twitch API для BroadcasterId {BroadcasterId}: локальный статус Online, но API вернул Offline", _broadcasterUserId);
            }
            else if (CurrentStatus != newStatus)
            {
                _logger.LogInformation("Статус стрима изменился с {OldStatus} на {NewStatus} для BroadcasterId {BroadcasterId}", CurrentStatus, newStatus, _broadcasterUserId);
                CurrentStatus = newStatus;
                await PublishStatusTransitionAsync(newStatus).ConfigureAwait(false);
            }

            if (isOnline && stream != null)
            {
                CurrentStream = new()
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

                _logger.LogDebug("Метаданные стрима обновлены для BroadcasterId {BroadcasterId}. StreamId: {StreamId}, Игра: {GameName}", _broadcasterUserId, stream.Id, stream.GameName);
            }
            else
            {
                CurrentStream = null;
            }

            PublishMonitoringLog(isOnline
                ? "Текущий статус: онлайн (по данным API)"
                : "Текущий статус: офлайн (по данным API)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить текущий статус стрима для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringError($"Ошибка получения текущего статуса стрима: {SafeMessage(ex)}");
        }
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _reconnectionPolicy.Reset();
        _logger.LogInformation("EventSub WebSocket подключен. SessionId: {SessionId}", args.SessionId);
        PublishMonitoringLog($"EventSub WebSocket подключен (Session: {args.SessionId})");

        if (!string.IsNullOrEmpty(_broadcasterUserId))
        {
            await CreateEventSubSubscriptionsAsync(args.SessionId, cancellationToken);
        }
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
                _logger.LogDebug("EventSub уведомление неизвестного типа: {Type}", args.SubscriptionType);
                break;
        }
    }

    private Task OnSessionReconnectAsync(EventSubReconnectArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub session_reconnect → {Url} (старый session: {OldSession})", args.ReconnectUrl, args.OldSessionId);
        PublishMonitoringLog($"EventSub переподключение (Session: {args.OldSessionId} → новый)...");
        return Task.CompletedTask;
    }

    private async Task OnRevocationAsync(EventSubRevocationArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub revocation: id={Id}, type={Type}, status={Status}", args.SubscriptionId, args.SubscriptionType, args.Status);
        PublishMonitoringLog($"EventSub подписка отозвана: {args.SubscriptionType} (status: {args.Status})");

        if (string.IsNullOrEmpty(_broadcasterUserId) || string.IsNullOrEmpty(_eventSubClient.SessionId))
        {
            return;
        }

        try
        {
            var condition = new Dictionary<string, string>
            {
                { "broadcaster_user_id", _broadcasterUserId },
            };

            var newId = await _helix.CreateEventSubSubscriptionAsync(args.SubscriptionType,
                "1",
                condition,
                _eventSubClient.SessionId,
                cancellationToken);

            _logger.LogInformation("Подписка {Type} восстановлена после revocation. Новый id: {Id}", args.SubscriptionType, newId);
            PublishMonitoringLog($"Подписка {args.SubscriptionType} восстановлена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить подписку {Type} после revocation", args.SubscriptionType);
            PublishMonitoringError($"Ошибка восстановления подписки {args.SubscriptionType}: {SafeMessage(ex)}");
        }
    }

    private async Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub WebSocket отключен. Причина: {Reason}. Disposed: {IsDisposed}, StopRequested: {IsStopRequested}", args.Reason, _disposed, _stopRequested);
        PublishMonitoringLog("EventSub WebSocket отключен");
        CurrentStatus = StreamStatus.Unknown;

        if (_disposed || _stopRequested)
        {
            return;
        }

        if (!_reconnectionPolicy.TryNextAttempt(out var delay))
        {
            _logger.LogError("Превышено максимальное количество попыток переподключения ({MaxAttempts}) для EventSub WebSocket. Остановка", _reconnectionPolicy.MaxAttempts);
            PublishMonitoringError($"Превышено максимальное количество попыток переподключения ({_reconnectionPolicy.MaxAttempts}). Мониторинг стрима остановлен.");

            lock (_lockObj)
            {
                _stopRequested = true;
            }

            SetMonitoringStopped();
            return;
        }

        var attempt = _reconnectionPolicy.CurrentAttempt;
        var maxAttempts = _reconnectionPolicy.MaxAttempts;

        _logger.LogWarning("Попытка переподключения EventSub {Attempt}/{MaxAttempts} через {DelayMs}мс...", attempt, maxAttempts, (int)delay.TotalMilliseconds);
        PublishMonitoringLog($"Попытка переподключения {attempt}/{maxAttempts} через {(int)delay.TotalSeconds} сек...");

        CancellationToken token;
        lock (_lockObj)
        {
            CancelAndResetReconnectToken();
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            token = _reconnectCts.Token;
        }

        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Ожидание переподключения отменено для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            return;
        }

        try
        {
            await _eventSubClient.StartAsync(token);
            _logger.LogInformation("Переподключение EventSub WebSocket инициировано (попытка {Attempt})", attempt);
            PublishMonitoringLog("Переподключение инициировано...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Исключение при попытке переподключения {Attempt}/{MaxAttempts}", attempt, maxAttempts);
            PublishMonitoringError($"Ошибка переподключения (попытка {attempt}/{maxAttempts}): {SafeMessage(ex)}");
        }
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

    private async Task HandleStreamOnlineAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var data = payload.Deserialize<EventSubStreamOnlinePayloadDto>(options);

        _logger.LogInformation("Событие стрима ONLINE получено через EventSub для BroadcasterId: {BroadcasterId}, Тип: {EventType}", _broadcasterUserId, data?.Event?.Type);
        PublishMonitoringLog($"Стрим запущен (EventSub): {data?.Event?.Type ?? "live"}");

        if (CurrentStatus != StreamStatus.Online)
        {
            CurrentStatus = StreamStatus.Online;
            await PublishStatusTransitionAsync(StreamStatus.Online).ConfigureAwait(false);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Запуск опроса метаданных стрима для BroadcasterId: {BroadcasterId}", _broadcasterUserId);

                for (var i = 0; i < 6; i++)
                {
                    _disposeCts.Token.ThrowIfCancellationRequested();

                    await RefreshCurrentStatusAsync();

                    if (CurrentStream != null)
                    {
                        _logger.LogInformation("Метаданные стрима успешно получены из API для BroadcasterId: {BroadcasterId} на попытке {Attempt}", _broadcasterUserId, i + 1);
                        PublishMonitoringLog("Метаданные стрима успешно получены из API");
                        await PublishStatusTransitionAsync(CurrentStatus).ConfigureAwait(false);
                        break;
                    }

                    if (CurrentStatus != StreamStatus.Online)
                    {
                        _logger.LogDebug("Стрим больше не онлайн, прерывание опроса метаданных для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
                        break;
                    }

                    var delaySeconds = 5 * (i + 1);
                    _logger.LogWarning("Метаданные еще не доступны в API. Повтор через {DelaySeconds}с (Попытка {Attempt}/6)...", delaySeconds, i + 1);
                    PublishMonitoringLog($"Метаданные еще не доступны в API. Повтор через {delaySeconds} сек (попытка {i + 1}/6)...");

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _disposeCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Опрос метаданных стрима отменен из-за завершения работы менеджера");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Непредвиденная ошибка во время фонового опроса метаданных стрима");
            }
        }, _disposeCts.Token);
    }

    private async Task HandleStreamOfflineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Событие стрима OFFLINE получено через EventSub для BroadcasterId: {BroadcasterId}", _broadcasterUserId);

        var oldStatus = CurrentStatus;
        CurrentStatus = StreamStatus.Offline;
        PublishMonitoringLog("Стрим завершен (EventSub)");
        CurrentStream = null;

        if (oldStatus != CurrentStatus)
        {
            await PublishStatusTransitionAsync(StreamStatus.Offline).ConfigureAwait(false);
        }
    }

    private Task PublishStatusTransitionAsync(StreamStatus newStatus)
    {
        var channel = _channelName;

        if (string.IsNullOrWhiteSpace(channel))
        {
            _logger.LogDebug("Публикация события статуса стрима пропущена: имя канала неизвестно");
            return Task.CompletedTask;
        }

        return newStatus switch
        {
            StreamStatus.Online => _eventBus.PublishAsync(new StreamWentOnline(channel, CurrentStream)),
            StreamStatus.Offline => _eventBus.PublishAsync(new StreamWentOffline(channel)),
            _ => Task.CompletedTask,
        };
    }

    private async Task<string?> GetBroadcasterUserIdAsync(string channelName)
    {
        _logger.LogDebug("Получение Broadcaster User ID для канала: {ChannelName}", channelName);

        if (string.IsNullOrEmpty(channelName))
        {
            _logger.LogWarning("Невозможно получить User ID: имя канала пустое");
            PublishMonitoringError("Имя канала не может быть пустым");
            return null;
        }

        try
        {
            PublishMonitoringLog($"Получение ID пользователя для канала: {channelName}");

            var user = await _helix.GetUserByLoginAsync(channelName);

            if (user == null)
            {
                _logger.LogWarning("Пользователь не найден для канала: {ChannelName}", channelName);
                PublishMonitoringError($"Пользователь с именем '{channelName}' не найден");
                return null;
            }

            var userId = user.Id;
            _logger.LogInformation("Канал {ChannelName} успешно разрешен в User ID {UserId}", channelName, userId);
            PublishMonitoringLog($"ID пользователя получен: {userId}");

            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении Broadcaster User ID для канала: {ChannelName}", channelName);
            PublishMonitoringError($"Ошибка получения ID пользователя: {SafeMessage(ex)}");
            return null;
        }
    }

    private async Task CreateEventSubSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_broadcasterUserId))
        {
            _logger.LogWarning("Невозможно создать подписки EventSub: BroadcasterUserId пуст");
            return;
        }

        _logger.LogDebug("Создание подписок EventSub для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
        PublishMonitoringLog("Создание подписок EventSub...");

        var subscriptionsCreated = 0;
        var condition = new Dictionary<string, string>
        {
            { "broadcaster_user_id", _broadcasterUserId },
        };

        try
        {
            PublishMonitoringLog("Создание подписки stream.online...");

            var subscriptionId = await _helix.CreateEventSubSubscriptionAsync("stream.online",
                "1",
                condition,
                sessionId,
                cancellationToken);

            subscriptionsCreated++;

            _logger.LogInformation("Успешно создана подписка 'stream.online'. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}", subscriptionId, sessionId);
            PublishMonitoringLog($"Подписка на stream.online создана (ID: {subscriptionId})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании подписки 'stream.online' для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringError($"Ошибка создания подписки stream.online: {SafeMessage(ex)}");
        }

        try
        {
            PublishMonitoringLog("Создание подписки stream.offline...");

            var subscriptionId = await _helix.CreateEventSubSubscriptionAsync("stream.offline",
                "1",
                condition,
                sessionId,
                cancellationToken);

            subscriptionsCreated++;

            _logger.LogInformation("Успешно создана подписка 'stream.offline'. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}", subscriptionId, sessionId);
            PublishMonitoringLog($"Подписка на stream.offline создана (ID: {subscriptionId})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании подписки 'stream.offline' для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringError($"Ошибка создания подписки stream.offline: {SafeMessage(ex)}");
        }

        if (subscriptionsCreated == 2)
        {
            _logger.LogInformation("Все необходимые подписки EventSub успешно созданы для BroadcasterId: {BroadcasterId}", _broadcasterUserId);
            PublishMonitoringLog("Все подписки EventSub созданы успешно");
        }
        else
        {
            _logger.LogWarning("Успешно создано только {CreatedCount}/2 подписок EventSub для BroadcasterId: {BroadcasterId}", subscriptionsCreated, _broadcasterUserId);
        }
    }

    private void CancelAndResetReconnectToken()
    {
        if (_reconnectCts == null)
        {
            return;
        }

        _reconnectCts.Cancel();
        _reconnectCts.Dispose();
        _reconnectCts = null;
    }

    private void SetMonitoringStopped()
    {
        lock (_lockObj)
        {
            _isMonitoring = false;
        }
    }

    private void PublishMonitoringLog(string message)
    {
        _ = _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Debug, "StreamMonitoring", $"[Monitoring] {message}"));
    }

    private void PublishMonitoringError(string message)
    {
        _ = _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Error, "StreamMonitoring", $"Ошибка EventSub: {message}"));
    }
}
