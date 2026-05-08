using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Reconnection;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.EventSub;

public sealed class EventSubConnectionHost :
    IStreamHostedComponent,
    IEventHandler<TwitchAuthorizationRefreshed>,
    IEventSubscriber,
    IAsyncDisposable
{
    private const int MaxReconnectAttempts = 5;

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });

    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventSubConnectionHost> _logger;
    private readonly ExponentialBackoffPolicy _reconnectionPolicy = new(MaxReconnectAttempts);
    private readonly IDisposable _authSubscription;

    private readonly object _lockObj = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _reconnectCts;
    private bool _stopRequested;
    private bool _disposed;

    public EventSubConnectionHost(
        ITwitchEventSubClient eventSubClient,
        IEventBus eventBus,
        ILogger<EventSubConnectionHost> logger)
    {
        _eventSubClient = eventSubClient;
        _eventBus = eventBus;
        _logger = logger;

        _eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
        _eventSubClient.OnDisconnected += OnDisconnectedAsync;
        _authSubscription = _eventBus.Subscribe(this);
    }

    public string Name => "EventSub WebSocket";

    public int StartOrder => 275;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource cts;

        lock (_lockObj)
        {
            if (_runCts is { IsCancellationRequested: false })
            {
                _logger.LogWarning("EventSubConnectionHost уже запущен");
                return;
            }

            _runCts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCts = cts;
            _stopRequested = false;
        }

        _reconnectionPolicy.Reset();

        await PublishStatusAsync(StreamMonitoringStatus.Connecting).ConfigureAwait(false);
        _logger.LogInformation("Подключение к EventSub WebSocket...");

        try
        {
            await _eventSubClient.StartAsync(cts.Token);
            _logger.LogInformation("Соединение EventSub инициировано. Ожидание подтверждения подключения...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка запуска EventSub WebSocket");
            throw;
        }
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;

        lock (_lockObj)
        {
            _stopRequested = true;
            CancelAndResetReconnectToken();
            cts = _runCts;
            _runCts = null;
        }

        if (cts != null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        _logger.LogInformation("Отключение от EventSub WebSocket...");
        await PublishStatusAsync(StreamMonitoringStatus.Disconnected).ConfigureAwait(false);

        try
        {
            await _eventSubClient.StopAsync(cancellationToken);
            _logger.LogInformation("EventSub WebSocket остановлен");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка остановки EventSub WebSocket");
        }
    }

    public async Task HandleAsync(TwitchAuthorizationRefreshed @event, CancellationToken cancellationToken)
    {
        if (@event.Role != TwitchOAuthRole.Bot)
        {
            _logger.LogDebug("TwitchAuthorizationRefreshed для роли {Role} игнорируется EventSubConnectionHost (ожидался Bot)", @event.Role);
            return;
        }

        if (_disposed)
        {
            _logger.LogDebug("TwitchAuthorizationRefreshed получен после dispose — игнорируется");
            return;
        }

        bool isRunning;
        lock (_lockObj)
        {
            isRunning = _runCts is { IsCancellationRequested: false };
        }

        if (isRunning)
        {
            _logger.LogInformation("Перезапуск EventSub WebSocket после обновления авторизации бота");
            await StopAsync(NullProgress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("Запуск EventSub WebSocket после получения авторизации бота");
        }

        await StartAsync(NullProgress, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _authSubscription.Dispose();
        _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
        _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

        lock (_lockObj)
        {
            _stopRequested = true;
            CancelAndResetReconnectToken();
        }

        if (_runCts != null)
        {
            await _runCts.CancelAsync();
            _runCts.Dispose();
            _runCts = null;
        }
    }

    private Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _reconnectionPolicy.Reset();
        _logger.LogInformation("EventSub session welcome (SessionId: {SessionId}) — статус мониторинга: Connected", args.SessionId);
        return PublishStatusAsync(StreamMonitoringStatus.Connected);
    }

    private async Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub WebSocket отключен. Причина: {Reason}. Disposed: {IsDisposed}, StopRequested: {IsStopRequested}",
            args.Reason, _disposed, _stopRequested);

        if (_disposed || _stopRequested)
        {
            _logger.LogDebug("Переподключение EventSub пропущено: disposed={IsDisposed}, stopRequested={IsStopRequested}", _disposed, _stopRequested);
            return;
        }

        if (!_reconnectionPolicy.TryNextAttempt(out var delay))
        {
            _logger.LogError("Превышено максимальное количество попыток переподключения ({MaxAttempts}). EventSub остановлен.",
                _reconnectionPolicy.MaxAttempts);

            await PublishStatusAsync(StreamMonitoringStatus.Failed).ConfigureAwait(false);

            lock (_lockObj)
            {
                _stopRequested = true;
            }

            return;
        }

        var attempt = _reconnectionPolicy.CurrentAttempt;
        var maxAttempts = _reconnectionPolicy.MaxAttempts;

        _logger.LogWarning("Попытка переподключения EventSub {Attempt}/{MaxAttempts} через {DelaySeconds} сек...",
            attempt, maxAttempts, (int)delay.TotalSeconds);

        await PublishStatusAsync(StreamMonitoringStatus.Reconnecting, $"Попытка {attempt}/{maxAttempts}").ConfigureAwait(false);

        CancellationToken token;
        lock (_lockObj)
        {
            if (_runCts == null)
            {
                _logger.LogDebug("Переподключение EventSub отменено: _runCts уже null (видимо, StopAsync прошёл одновременно)");
                return;
            }

            CancelAndResetReconnectToken();
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_runCts.Token);
            token = _reconnectCts.Token;
        }

        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Задержка перед переподключением EventSub отменена (попытка {Attempt}/{MaxAttempts})", attempt, maxAttempts);
            return;
        }

        try
        {
            await _eventSubClient.StartAsync(token);
            _logger.LogInformation("Переподключение EventSub WebSocket инициировано (попытка {Attempt})", attempt);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Переподключение EventSub отменено (попытка {Attempt}/{MaxAttempts})", attempt, maxAttempts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка переподключения EventSub (попытка {Attempt}/{MaxAttempts})", attempt, maxAttempts);
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

    private Task PublishStatusAsync(StreamMonitoringStatus status, string? detail = null)
    {
        _logger.LogDebug("Публикация StreamMonitoringStatusChanged: {Status} ({Detail})", status, detail ?? "—");
        return _eventBus.PublishAsync(new StreamMonitoringStatusChanged(status, detail));
    }
}
