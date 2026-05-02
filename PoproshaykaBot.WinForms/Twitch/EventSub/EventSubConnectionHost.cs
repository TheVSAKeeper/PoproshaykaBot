using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Infrastructure.Reconnection;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Twitch.EventSub;

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

        PublishStatus(StreamMonitoringStatus.Connecting);
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
        PublishStatus(StreamMonitoringStatus.Disconnected);

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
            return;
        }

        if (_disposed)
        {
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
        PublishStatus(StreamMonitoringStatus.Connected);
        return Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub WebSocket отключен. Причина: {Reason}. Disposed: {IsDisposed}, StopRequested: {IsStopRequested}",
            args.Reason, _disposed, _stopRequested);

        if (_disposed || _stopRequested)
        {
            return;
        }

        if (!_reconnectionPolicy.TryNextAttempt(out var delay))
        {
            _logger.LogError("Превышено максимальное количество попыток переподключения ({MaxAttempts}). EventSub остановлен.",
                _reconnectionPolicy.MaxAttempts);

            PublishStatus(StreamMonitoringStatus.Failed);

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

        PublishStatus(StreamMonitoringStatus.Reconnecting, $"Попытка {attempt}/{maxAttempts}");

        CancellationToken token;
        lock (_lockObj)
        {
            if (_runCts == null)
            {
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
            return;
        }

        try
        {
            await _eventSubClient.StartAsync(token);
            _logger.LogInformation("Переподключение EventSub WebSocket инициировано (попытка {Attempt})", attempt);
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

    private void PublishStatus(StreamMonitoringStatus status, string? detail = null)
    {
        _ = _eventBus.PublishAsync(new StreamMonitoringStatusChanged(status, detail));
    }
}
