using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Reconnection;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Twitch.EventSub;

public sealed class EventSubConnectionHost :
    IStreamHostedComponent,
    IEventHandler<PlatformAuthorizationRefreshed>,
    IEventHandler<ActiveChannelChanged>,
    IAsyncDisposable
{
    private const int MaxReconnectAttempts = 5;
    private const int ConnectionUnusedCloseCode = 4003;

    private readonly PlatformAuthRole _role;
    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly AccountsStore _accountsStore;
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventSubConnectionHost> _logger;
    private readonly TwitchChannelAuthorization? _channelAuthorization;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly SettingsManager? _settingsManager;
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly ExponentialBackoffPolicy _reconnectionPolicy = new(MaxReconnectAttempts);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly IDisposable _authSubscription;
    private readonly IDisposable _activeChannelSubscription;

    private readonly Lock _lockObj = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _reconnectCts;
    private bool _stopRequested;
    private bool _monitoringActive;
    private bool _disposed;
    private bool _lastTwitchEnabled;
    private string? _activeContextId;
    private int _unusedCloseStreak;
    private StreamMonitoringStatus? _status;
    private long _lastMessageAtUtcTicks;

    public EventSubConnectionHost(
        PlatformAuthRole role,
        ITwitchEventSubClient eventSubClient,
        AccountsStore accountsStore,
        IEventBus eventBus,
        ILogger<EventSubConnectionHost> logger,
        TwitchChannelAuthorization? channelAuthorization = null,
        ChannelCatalogStore? channelCatalogStore = null,
        SettingsManager? settingsManager = null,
        ITargetChannelProvider? targetChannelProvider = null)
    {
        _role = role;
        _eventSubClient = eventSubClient;
        _accountsStore = accountsStore;
        _eventBus = eventBus;
        _logger = logger;
        _channelAuthorization = channelAuthorization;
        _channelCatalogStore = channelCatalogStore;
        _settingsManager = settingsManager;
        _targetChannelProvider = targetChannelProvider;
        _lastTwitchEnabled = IsTwitchEnabled();

        _eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
        _eventSubClient.OnNotification += OnNotificationAsync;
        _eventSubClient.OnDisconnected += OnDisconnectedAsync;
        _authSubscription = _eventBus.Subscribe<PlatformAuthorizationRefreshed>(this);
        _activeChannelSubscription = _eventBus.Subscribe<ActiveChannelChanged>(this);
        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed += OnCatalogChanged;
        }
        if (_settingsManager is not null)
        {
            _settingsManager.SettingsSaved += OnSettingsSaved;
        }
    }

    public PlatformAuthRole Role => _role;

    public StreamMonitoringStatus? CurrentStatus
    {
        get
        {
            lock (_lockObj)
            {
                return _status;
            }
        }
    }

    public DateTimeOffset? LastMessageAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastMessageAtUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public int ReconnectAttempt => _reconnectionPolicy.CurrentAttempt;

    public int ReconnectLimit => _reconnectionPolicy.MaxAttempts;

    public string Name => _role == PlatformAuthRole.Bot
        ? "EventSub WebSocket (бот)"
        : "EventSub WebSocket (broadcaster)";

    public int StartOrder => _role == PlatformAuthRole.Bot ? 275 : 276;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            lock (_lockObj)
            {
                _monitoringActive = true;
            }

            if (!IsTwitchEnabled())
            {
                _logger.LogInformation("EventSub WebSocket ({Role}): запуск отложен – площадка Twitch выключена.", _role);
                return;
            }

            if (!TryResolveActiveContext(out var contextId))
            {
                _logger.LogInformation("EventSub WebSocket ({Role}): запуск отложен – нет access-токена для роли. Ждём PlatformAuthorizationRefreshed.", _role);
                return;
            }

            await StartInternalAsync(contextId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_lockObj)
            {
                _monitoringActive = false;
            }

            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;

        lock (_lockObj)
        {
            _stopRequested = true;
            _activeContextId = null;
            CancelAndResetReconnectToken();
            cts = _runCts;
            _runCts = null;
        }

        if (cts != null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        _logger.LogInformation("Отключение от EventSub WebSocket ({Role})...", _role);
        await PublishStatusAsync(StreamMonitoringStatus.Disconnected).ConfigureAwait(false);

        try
        {
            await _eventSubClient.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка остановки EventSub WebSocket ({Role})", _role);
        }
    }

    public async Task HandleAsync(PlatformAuthorizationRefreshed @event, CancellationToken cancellationToken)
    {
        if (@event.Platform != StreamingPlatform.Twitch || @event.Role != _role)
        {
            _logger.LogDebug("PlatformAuthorizationRefreshed ({Platform}, {Role}) игнорируется EventSubConnectionHost ({HostRole})",
                @event.Platform, @event.Role, _role);
            return;
        }

        if (_disposed)
        {
            _logger.LogDebug("PlatformAuthorizationRefreshed получен после dispose – игнорируется ({Role})", _role);
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            bool isRunning;
            lock (_lockObj)
            {
                isRunning = _runCts is { IsCancellationRequested: false };
            }

            if (!TryResolveActiveContext(out var contextId))
            {
                if (isRunning)
                {
                    _logger.LogInformation("Остановка EventSub WebSocket ({Role}) – access-токен отсутствует", _role);
                    await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("PlatformAuthorizationRefreshed ({Role}): запуск EventSub WebSocket отложен – нет access-токена", _role);
                }

                return;
            }

            if (isRunning)
            {
                _logger.LogInformation("Перезапуск EventSub WebSocket ({Role}) после обновления авторизации", _role);
                await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation("Запуск EventSub WebSocket ({Role}) после получения авторизации", _role);
            }

            await StartInternalAsync(contextId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task HandleAsync(ActiveChannelChanged @event, CancellationToken cancellationToken)
    {
        if (@event.Platform != StreamingPlatform.Twitch || _disposed)
        {
            return Task.CompletedTask;
        }

        return ReconcileContextAsync(forceRestartBroadcaster: true, cancellationToken);
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = ReconcileCatalogChangeAsync();
    }

    private void OnSettingsSaved()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lockObj)
        {
            var currentEnabled = IsTwitchEnabled();
            if (currentEnabled == _lastTwitchEnabled)
            {
                return;
            }

            _lastTwitchEnabled = currentEnabled;
        }

        _ = ReconcileCatalogChangeAsync();
    }

    private async Task ReconcileCatalogChangeAsync()
    {
        try
        {
            await ReconcileContextAsync(forceRestartBroadcaster: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventSub WebSocket ({Role}): ошибка согласования контекста после изменения каталога каналов", _role);
        }
    }

    private async Task ReconcileContextAsync(bool forceRestartBroadcaster, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            bool isRunning;
            bool monitoringActive;
            string? activeContextId;
            lock (_lockObj)
            {
                isRunning = _runCts is { IsCancellationRequested: false };
                monitoringActive = _monitoringActive;
                activeContextId = _activeContextId;
            }

            if (!monitoringActive && !isRunning)
            {
                return;
            }

            var hasToken = TryResolveActiveContext(out var nextContextId);

            if (_role == PlatformAuthRole.Broadcaster)
            {
                if (!hasToken)
                {
                    if (isRunning)
                    {
                        _logger.LogInformation("Остановка EventSub WebSocket ({Role}) – активный канал переключён на режим без авторизации стримера", _role);
                        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }

                if (isRunning)
                {
                    if (!forceRestartBroadcaster && string.Equals(activeContextId, nextContextId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _logger.LogInformation("Перезапуск EventSub WebSocket ({Role}) после смены активного канала", _role);
                    await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation("Запуск EventSub WebSocket ({Role}) после смены активного канала", _role);
                }

                await StartInternalAsync(nextContextId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!hasToken)
            {
                if (isRunning)
                {
                    _logger.LogInformation("Остановка EventSub WebSocket ({Role}) – нет авторизации бота после смены канала", _role);
                    await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            if (isRunning && !string.Equals(activeContextId, nextContextId, StringComparison.Ordinal))
            {
                _logger.LogInformation("Перезапуск EventSub WebSocket ({Role}) – сменился контекст бота ({OldContext} → {NewContext})",
                    _role, activeContextId ?? "–", nextContextId);
                await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                await StartInternalAsync(nextContextId, cancellationToken).ConfigureAwait(false);
            }
            else if (!isRunning && monitoringActive)
            {
                await StartInternalAsync(nextContextId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _authSubscription.Dispose();
        _activeChannelSubscription.Dispose();
        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed -= OnCatalogChanged;
        }
        if (_settingsManager is not null)
        {
            _settingsManager.SettingsSaved -= OnSettingsSaved;
        }
        _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
        _eventSubClient.OnNotification -= OnNotificationAsync;
        _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

        CancellationTokenSource? runCts;
        lock (_lockObj)
        {
            _monitoringActive = false;
            _stopRequested = true;
            CancelAndResetReconnectToken();
            runCts = _runCts;
            _runCts = null;
        }

        if (runCts != null)
        {
            await runCts.CancelAsync();
            runCts.Dispose();
        }
    }

    private Task OnNotificationAsync(EventSubNotificationArgs args, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastMessageAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Interlocked.Exchange(ref _unusedCloseStreak, 0);
        _reconnectionPolicy.Reset();
        return Task.CompletedTask;
    }

    private Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastMessageAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        if (Volatile.Read(ref _unusedCloseStreak) == 0)
        {
            _reconnectionPolicy.Reset();
        }

        _logger.LogInformation("EventSub session welcome ({Role}, SessionId: {SessionId}) – статус мониторинга: Connected", _role, args.SessionId);
        return PublishStatusAsync(StreamMonitoringStatus.Connected);
    }

    private async Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        if (_disposed || _stopRequested)
        {
            _logger.LogDebug("EventSub WebSocket ({Role}) отключен ({Reason}); переподключение пропущено: disposed={IsDisposed}, stopRequested={IsStopRequested}",
                _role, args.Reason, _disposed, _stopRequested);

            return;
        }

        if (!TryResolveActiveContext(out _))
        {
            _logger.LogInformation("EventSub WebSocket ({Role}) отключен ({Reason}); переподключение отменено – нет авторизации роли для активного канала",
                _role, args.Reason);

            lock (_lockObj)
            {
                _stopRequested = true;
                _activeContextId = null;
                CancelAndResetReconnectToken();
                _runCts?.Dispose();
                _runCts = null;
            }

            await PublishStatusAsync(StreamMonitoringStatus.Disconnected).ConfigureAwait(false);
            return;
        }

        if (args.CloseCode == ConnectionUnusedCloseCode)
        {
            Interlocked.Increment(ref _unusedCloseStreak);
        }
        else
        {
            Interlocked.Exchange(ref _unusedCloseStreak, 0);
        }

        if (!_reconnectionPolicy.TryNextAttempt(out var delay))
        {
            _logger.LogError("EventSub WebSocket ({Role}) отключен ({Reason}); превышено максимальное количество попыток переподключения ({MaxAttempts}). Остановлен.",
                _role, args.Reason, _reconnectionPolicy.MaxAttempts);

            await PublishStatusAsync(StreamMonitoringStatus.Failed).ConfigureAwait(false);

            lock (_lockObj)
            {
                _stopRequested = true;
                _activeContextId = null;
            }

            return;
        }

        var attempt = _reconnectionPolicy.CurrentAttempt;
        var maxAttempts = _reconnectionPolicy.MaxAttempts;

        _logger.LogWarning("EventSub WebSocket ({Role}) отключен ({Reason}); попытка переподключения {Attempt}/{MaxAttempts} через {DelaySeconds} сек...",
            _role, args.Reason, attempt, maxAttempts, (int)delay.TotalSeconds);

        await PublishStatusAsync(StreamMonitoringStatus.Reconnecting, $"Попытка {attempt}/{maxAttempts}").ConfigureAwait(false);

        CancellationToken token;
        lock (_lockObj)
        {
            if (_runCts == null)
            {
                _logger.LogDebug("Переподключение EventSub ({Role}) отменено: _runCts уже null (видимо, StopAsync прошёл одновременно)", _role);
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
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Задержка перед переподключением EventSub ({Role}) отменена (попытка {Attempt}/{MaxAttempts})", _role, attempt, maxAttempts);
            return;
        }

        try
        {
            await _eventSubClient.StartAsync(token);
            _logger.LogInformation("Переподключение EventSub WebSocket ({Role}) инициировано (попытка {Attempt})", _role, attempt);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Переподключение EventSub ({Role}) отменено (попытка {Attempt}/{MaxAttempts})", _role, attempt, maxAttempts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка переподключения EventSub ({Role}) (попытка {Attempt}/{MaxAttempts})", _role, attempt, maxAttempts);
        }
    }

    private bool IsTwitchEnabled() =>
        _targetChannelProvider?.Current.IsDebugSession == true
        || _settingsManager?.Current?.Twitch?.Enabled != false;

    private bool TryResolveActiveContext(out string contextId)
    {
        contextId = AuthContextIds.Default;

        if (!IsTwitchEnabled())
        {
            return false;
        }

        try
        {
            if (_channelAuthorization is not null)
            {
                if (_channelAuthorization.TryGetAccount(_role, out var resolvedContext, out var account)
                    && !string.IsNullOrEmpty(account.AccessToken))
                {
                    contextId = resolvedContext;
                    return true;
                }

                if (_role == PlatformAuthRole.Broadcaster)
                {
                    return false;
                }
            }

            return !string.IsNullOrEmpty(_accountsStore.Load(_role).AccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventSubConnectionHost ({Role}): не удалось прочитать AccountsStore – считаем, что токена нет", _role);
            return false;
        }
    }

    private async Task StartInternalAsync(string contextId, CancellationToken cancellationToken)
    {
        CancellationTokenSource cts;

        lock (_lockObj)
        {
            if (_runCts is { IsCancellationRequested: false })
            {
                _logger.LogWarning("EventSubConnectionHost ({Role}) уже запущен", _role);
                return;
            }

            _runCts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCts = cts;
            _stopRequested = false;
            _activeContextId = contextId;
        }

        Interlocked.Exchange(ref _unusedCloseStreak, 0);
        _reconnectionPolicy.Reset();

        await PublishStatusAsync(StreamMonitoringStatus.Connecting).ConfigureAwait(false);
        _logger.LogInformation("Подключение к EventSub WebSocket ({Role})...", _role);

        try
        {
            await _eventSubClient.StartAsync(cts.Token);
            _logger.LogInformation("Соединение EventSub ({Role}) инициировано. Ожидание подтверждения подключения...", _role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка запуска EventSub WebSocket ({Role})", _role);
            throw new InvalidOperationException($"Не удалось запустить EventSub WebSocket ({_role})", ex);
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
        lock (_lockObj)
        {
            _status = status;
        }

        _logger.LogDebug("Публикация StreamMonitoringStatusChanged: {Role} {Status} ({Detail})", _role, status, detail ?? "–");
        return _eventBus.PublishAsync(
            new StreamMonitoringStatusChanged(Platforms.StreamingPlatform.Twitch, _role, status, detail),
            CancellationToken.None);
    }
}
