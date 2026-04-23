using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Streaming;

public class StreamStatusManager : IStreamStatus, IAsyncDisposable
{
    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly ITwitchHelixClient _helix;
    private readonly IBroadcasterIdProvider _broadcasterIdProvider;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StreamStatusManager> _logger;

    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public StreamStatusManager(
        ITwitchEventSubClient eventSubClient,
        ITwitchHelixClient helix,
        IBroadcasterIdProvider broadcasterIdProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<StreamStatusManager> logger)
    {
        _eventSubClient = eventSubClient;
        _helix = helix;
        _broadcasterIdProvider = broadcasterIdProvider;
        _settingsManager = settingsManager;
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Уничтожение ресурсов StreamStatusManager");
        _disposed = true;

        await _disposeCts.CancelAsync();
        _disposeCts.Dispose();

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
            var broadcasterId = await _broadcasterIdProvider.GetAsync(_disposeCts.Token);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogDebug("Обновление статуса пропущено: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Запрос текущего статуса стрима через API для BroadcasterId: {BroadcasterId}", broadcasterId);
            var stream = await _helix.GetStreamAsync(broadcasterId);

            var isOnline = stream != null;
            var newStatus = isOnline ? StreamStatus.Online : StreamStatus.Offline;

            if (CurrentStatus == StreamStatus.Online && newStatus == StreamStatus.Offline)
            {
                _logger.LogWarning("Обнаружена задержка Twitch API для BroadcasterId {BroadcasterId}: локальный статус Online, но API вернул Offline", broadcasterId);
            }
            else if (CurrentStatus != newStatus)
            {
                _logger.LogInformation("Статус стрима изменился с {OldStatus} на {NewStatus} для BroadcasterId {BroadcasterId}", CurrentStatus, newStatus, broadcasterId);
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

                _logger.LogDebug("Метаданные стрима обновлены для BroadcasterId {BroadcasterId}. StreamId: {StreamId}, Игра: {GameName}", broadcasterId, stream.Id, stream.GameName);
            }
            else
            {
                CurrentStream = null;
            }

            PublishMonitoringLog(isOnline
                ? "Текущий статус: онлайн (по данным API)"
                : "Текущий статус: офлайн (по данным API)");
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить текущий статус стрима");
            PublishMonitoringError($"Ошибка получения текущего статуса стрима: {SafeMessage(ex)}");
        }
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub WebSocket подключен. SessionId: {SessionId}", args.SessionId);
        PublishMonitoringLog($"EventSub WebSocket подключен (Session: {args.SessionId})");

        await RefreshCurrentStatusAsync();
        await CreateEventSubSubscriptionsAsync(args.SessionId, cancellationToken);
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

        if (string.IsNullOrEmpty(_eventSubClient.SessionId))
        {
            return;
        }

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
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
            PublishMonitoringLog($"Подписка {args.SubscriptionType} восстановлена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить подписку {Type} после revocation", args.SubscriptionType);
            PublishMonitoringError($"Ошибка восстановления подписки {args.SubscriptionType}: {SafeMessage(ex)}");
        }
    }

    private Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        CurrentStatus = StreamStatus.Unknown;
        return Task.CompletedTask;
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

        _logger.LogInformation("Событие стрима ONLINE получено через EventSub, Тип: {EventType}", data?.Event?.Type);
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
                _logger.LogDebug("Запуск опроса метаданных стрима");

                for (var i = 0; i < 6; i++)
                {
                    _disposeCts.Token.ThrowIfCancellationRequested();

                    await RefreshCurrentStatusAsync();

                    if (CurrentStream != null)
                    {
                        _logger.LogInformation("Метаданные стрима успешно получены из API на попытке {Attempt}", i + 1);
                        PublishMonitoringLog("Метаданные стрима успешно получены из API");
                        await PublishStatusTransitionAsync(CurrentStatus).ConfigureAwait(false);
                        break;
                    }

                    if (CurrentStatus != StreamStatus.Online)
                    {
                        _logger.LogDebug("Стрим больше не онлайн, прерывание опроса метаданных");
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
        _logger.LogInformation("Событие стрима OFFLINE получено через EventSub");

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
        var channel = _settingsManager.Current.Twitch.Channel;

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

    private async Task CreateEventSubSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            _logger.LogWarning("Невозможно создать подписки EventSub: BroadcasterId недоступен");
            PublishMonitoringError("Не удалось определить BroadcasterId для подписок EventSub");
            return;
        }

        _logger.LogDebug("Создание подписок EventSub для BroadcasterId: {BroadcasterId}", broadcasterId);
        PublishMonitoringLog("Создание подписок EventSub...");

        var subscriptionsCreated = 0;
        var condition = new Dictionary<string, string>
        {
            { "broadcaster_user_id", broadcasterId },
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
            _logger.LogError(ex, "Ошибка при создании подписки 'stream.online' для BroadcasterId: {BroadcasterId}", broadcasterId);
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
            _logger.LogError(ex, "Ошибка при создании подписки 'stream.offline' для BroadcasterId: {BroadcasterId}", broadcasterId);
            PublishMonitoringError($"Ошибка создания подписки stream.offline: {SafeMessage(ex)}");
        }

        if (subscriptionsCreated == 2)
        {
            _logger.LogInformation("Все необходимые подписки EventSub успешно созданы для BroadcasterId: {BroadcasterId}", broadcasterId);
            PublishMonitoringLog("Все подписки EventSub созданы успешно");
        }
        else
        {
            _logger.LogWarning("Успешно создано только {CreatedCount}/2 подписок EventSub для BroadcasterId: {BroadcasterId}", subscriptionsCreated, broadcasterId);
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
