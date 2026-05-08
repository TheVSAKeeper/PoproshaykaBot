using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Twitch.EventSub;

public sealed class TwitchEventSubClient(ILogger<TwitchEventSubClient> logger) : ITwitchEventSubClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan KeepaliveGrace = TimeSpan.FromSeconds(5);

    private readonly object _stateLock = new();

    private CancellationTokenSource? _internalCts;
    private Task? _runnerTask;
    private string? _currentUrl;
    private bool _reconnectRequested;

    private long _lastKeepaliveTicks = DateTime.UtcNow.Ticks;
    private TimeSpan _keepaliveTimeout = TimeSpan.FromSeconds(10);

    public event EventSubAsyncHandler<EventSubDisconnectedArgs>? OnDisconnected;

    public event EventSubAsyncHandler<EventSubNotificationArgs>? OnNotification;

    public event EventSubAsyncHandler<EventSubRevocationArgs>? OnRevocation;

    public event EventSubAsyncHandler<EventSubReconnectArgs>? OnSessionReconnect;

    public event EventSubAsyncHandler<EventSubSessionWelcomeArgs>? OnSessionWelcome;

    public string? SessionId
    {
        get
        {
            lock (_stateLock)
            {
                return field;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                field = value;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cts;

        lock (_stateLock)
        {
            if (_runnerTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _internalCts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _internalCts = cts;
            _currentUrl = TwitchEndpoints.EventSubWebSocket;
            _reconnectRequested = false;
            _runnerTask = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? runner;

        lock (_stateLock)
        {
            cts = _internalCts;
            runner = _runnerTask;
        }

        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync();

        if (runner is not null)
        {
            try
            {
                await runner.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_stateLock)
        {
            if (ReferenceEquals(_runnerTask, runner))
            {
                _runnerTask = null;
            }

            if (ReferenceEquals(_internalCts, cts))
            {
                _internalCts.Dispose();
                _internalCts = null;
            }
        }

        SessionId = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private static string MapDisconnectReason(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => "Остановка EventSub-клиента",
            WebSocketException or SocketException => "Соединение EventSub прервано",
            _ when ex.GetType().Namespace?.StartsWith("System.Net", StringComparison.Ordinal) == true
                   || ex.InnerException is WebSocketException or SocketException
                => "Соединение EventSub прервано",
            _ => "Неизвестная ошибка соединения EventSub",
        };
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string? disconnectReason = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var url = _currentUrl ?? TwitchEndpoints.EventSubWebSocket;
                _reconnectRequested = false;

                logger.LogInformation("Подключение EventSub WebSocket к {Url}", url);
                await RunSessionAsync(new(url), cancellationToken);

                if (!_reconnectRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EventSub соединение разорвано");
            disconnectReason = MapDisconnectReason(ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _ = SafeRaiseAsync(OnDisconnected, new(disconnectReason ?? "Сессия EventSub завершена"), CancellationToken.None);
    }

    private async Task RunSessionAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        await ws.ConnectAsync(uri, cancellationToken);
        Interlocked.Exchange(ref _lastKeepaliveTicks, DateTime.UtcNow.Ticks);

        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        var keepaliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var keepaliveTask = MonitorKeepaliveAsync(ws, keepaliveTimer, cancellationToken);

        try
        {
            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.LogInformation("EventSub получил Close: {Status} {Description}", result.CloseStatus, result.CloseStatusDescription);
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                Interlocked.Exchange(ref _lastKeepaliveTicks, DateTime.UtcNow.Ticks);

                var raw = sb.ToString();
                EventSubMessageDto? message;
                try
                {
                    message = JsonSerializer.Deserialize<EventSubMessageDto>(raw, JsonOptions);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "EventSub получил неразбираемое сообщение: {Raw}", raw);
                    continue;
                }

                if (message is null)
                {
                    continue;
                }

                await DispatchAsync(message, cancellationToken);
            }
        }
        finally
        {
            keepaliveTimer.Dispose();
            try
            {
                await keepaliveTask;
            }
            catch (OperationCanceledException)
            {
            }

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client shutdown", CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private async Task MonitorKeepaliveAsync(ClientWebSocket socket, PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var lastKeepalive = new DateTime(Interlocked.Read(ref _lastKeepaliveTicks), DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - lastKeepalive;
            if (elapsed > _keepaliveTimeout + KeepaliveGrace)
            {
                logger.LogWarning("Не получено keepalive {Elapsed} сек > порога {Limit} сек, форсируем reconnect", elapsed.TotalSeconds, (_keepaliveTimeout + KeepaliveGrace).TotalSeconds);
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "keepalive timeout", CancellationToken.None);
                }
                catch
                {
                }

                return;
            }
        }
    }

    private async Task DispatchAsync(EventSubMessageDto message, CancellationToken cancellationToken)
    {
        switch (message.Metadata.MessageType)
        {
            case "session_welcome":
                await HandleSessionWelcomeAsync(message, cancellationToken);
                break;

            case "session_keepalive":
                break;

            case "notification":
                await HandleNotificationAsync(message, cancellationToken);
                break;

            case "session_reconnect":
                await HandleSessionReconnectAsync(message, cancellationToken);
                break;

            case "revocation":
                await HandleRevocationAsync(message, cancellationToken);
                break;

            default:
                logger.LogWarning("EventSub: неизвестный message_type {Type}", message.Metadata.MessageType);
                break;
        }
    }

    private async Task HandleSessionWelcomeAsync(EventSubMessageDto message, CancellationToken cancellationToken)
    {
        var dto = message.Payload.Deserialize<EventSubSessionPayloadDto>(JsonOptions);
        if (dto is null)
        {
            return;
        }

        SessionId = dto.Session.Id;
        if (dto.Session.KeepaliveTimeoutSeconds is { } seconds and > 0)
        {
            _keepaliveTimeout = TimeSpan.FromSeconds(seconds);
        }

        logger.LogInformation("EventSub session_welcome: id={SessionId}, keepalive={Seconds} сек", dto.Session.Id, _keepaliveTimeout.TotalSeconds);
        await SafeRaiseAsync(OnSessionWelcome, new(dto.Session.Id, dto.Session.KeepaliveTimeoutSeconds), cancellationToken);
    }

    private async Task HandleNotificationAsync(EventSubMessageDto message, CancellationToken cancellationToken)
    {
        var subscriptionType = message.Metadata.SubscriptionType ?? string.Empty;
        var subscriptionVersion = message.Metadata.SubscriptionVersion ?? "1";
        await SafeRaiseAsync(OnNotification,
            new(subscriptionType, subscriptionVersion, message.Metadata.MessageId, message.Metadata.MessageTimestamp, message.Payload),
            cancellationToken);
    }

    private async Task HandleSessionReconnectAsync(EventSubMessageDto message, CancellationToken cancellationToken)
    {
        var dto = message.Payload.Deserialize<EventSubSessionPayloadDto>(JsonOptions);
        if (dto is null || string.IsNullOrEmpty(dto.Session.ReconnectUrl))
        {
            logger.LogWarning("session_reconnect без reconnect_url, обычная реконнект-логика");
            return;
        }

        var oldSessionId = SessionId ?? string.Empty;
        _currentUrl = dto.Session.ReconnectUrl;
        _reconnectRequested = true;
        logger.LogInformation("EventSub session_reconnect → {Url}", dto.Session.ReconnectUrl);
        await SafeRaiseAsync(OnSessionReconnect, new(dto.Session.ReconnectUrl, oldSessionId), cancellationToken);
    }

    private async Task HandleRevocationAsync(EventSubMessageDto message, CancellationToken cancellationToken)
    {
        var dto = message.Payload.Deserialize<EventSubRevocationPayloadDto>(JsonOptions);
        if (dto is null)
        {
            return;
        }

        logger.LogWarning("EventSub revocation: id={Id}, type={Type}, status={Status}", dto.Subscription.Id, dto.Subscription.Type, dto.Subscription.Status);
        await SafeRaiseAsync(OnRevocation, new(dto.Subscription.Id, dto.Subscription.Type, dto.Subscription.Status), cancellationToken);
    }

    private async Task SafeRaiseAsync<TArgs>(EventSubAsyncHandler<TArgs>? handler, TArgs args, CancellationToken cancellationToken)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<EventSubAsyncHandler<TArgs>>())
        {
            try
            {
                await subscriber(args, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Подписчик EventSub упал на типе {Type}", typeof(TArgs).Name);
            }
        }
    }
}
