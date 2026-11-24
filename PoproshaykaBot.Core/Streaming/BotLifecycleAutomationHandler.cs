using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Streaming;

public sealed class BotLifecycleAutomationHandler :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventHandler<PlatformConnectionChanged>,
    IEventHandler<ActiveChannelChanged>,
    IEventSubscriber,
    IDisposable
{
    private sealed class PlatformState
    {
        public BotLifecyclePhase Phase { get; set; } = BotLifecyclePhase.Idle;

        public bool IsStreamOnline { get; set; }
    }

    private readonly IBotConnectionController _connectionManager;
    private readonly IBotLifecycleAutomationSettingsProvider _settingsProvider;
    private readonly IBroadcastOfflineStopCoordinator _offlineStopCoordinator;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformChannelMonitor> _channelMonitors;
    private readonly ILogger<BotLifecycleAutomationHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly IDisposable _phaseSubscription;
    private readonly IDisposable _activeChannelSubscription;
    private readonly Lock _stateLock = new();
    private readonly Dictionary<StreamingPlatform, PlatformState> _states = [];

    public BotLifecycleAutomationHandler(
        IBotConnectionController connectionManager,
        IBotLifecycleAutomationSettingsProvider settingsProvider,
        IBroadcastOfflineStopCoordinator offlineStopCoordinator,
        ITargetChannelProvider targetChannelProvider,
        IEventBus eventBus,
        ILogger<BotLifecycleAutomationHandler> logger,
        IEnumerable<IPlatformChannelMonitor>? channelMonitors = null)
    {
        _connectionManager = connectionManager;
        _settingsProvider = settingsProvider;
        _offlineStopCoordinator = offlineStopCoordinator;
        _targetChannelProvider = targetChannelProvider;
        _channelMonitors = channelMonitors?.ToDictionary(static monitor => monitor.Platform) ?? [];
        _logger = logger;

        _onlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
        _phaseSubscription = eventBus.Subscribe<PlatformConnectionChanged>(this);
        _activeChannelSubscription = eventBus.Subscribe<ActiveChannelChanged>(this);
    }

    public Task HandleAsync(PlatformConnectionChanged @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            GetOrCreateStateLocked(@event.Platform).Phase = @event.Phase;
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ActiveChannelChanged @event, CancellationToken cancellationToken)
    {
        if (!_channelMonitors.TryGetValue(@event.Platform, out var monitor))
        {
            return Task.CompletedTask;
        }

        var snapshot = monitor.GetSnapshot(@event.NewChannel);
        BotLifecyclePhase phase;

        lock (_stateLock)
        {
            var state = GetOrCreateStateLocked(@event.Platform);
            state.IsStreamOnline = snapshot.Status == StreamStatus.Online;
            phase = state.Phase;
        }

        if (snapshot.Status == StreamStatus.Online)
        {
            TryAutoConnect(@event.Platform, @event.NewDisplayName, phase);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        BotLifecyclePhase phase;

        lock (_stateLock)
        {
            var state = GetOrCreateStateLocked(@event.Platform);
            state.IsStreamOnline = true;
            phase = state.Phase;
        }

        TryAutoConnect(@event.Platform, @event.Channel, phase);
        return Task.CompletedTask;
    }

    private void TryAutoConnect(StreamingPlatform platform, string channel, BotLifecyclePhase phase)
    {
        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            _logger.LogDebug("Авто-подключение {Platform} пропущено – режим отладки без записи данных", platform);
            return;
        }

        var settings = _settingsProvider.Get(platform);

        if (!settings.AutoConnectOnStreamOnline)
        {
            return;
        }

        if (!IsDisconnectedPhase(phase))
        {
            _logger.LogDebug("Авто-подключение {Platform} пропущено: текущая фаза {Phase}", platform, phase);
            return;
        }

        if (_connectionManager.IsPlatformBusy(platform))
        {
            _logger.LogDebug("Авто-подключение {Platform} пропущено: BotConnectionManager занят", platform);
            return;
        }

        try
        {
            _logger.LogInformation("🔴 Стрим онлайн ({Platform}, {Channel}) – автоматически подключаю бота", platform, channel);
            _connectionManager.StartConnection(platform);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Не удалось запустить авто-подключение бота для {Platform}", platform);
        }
    }

    public Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        BotLifecyclePhase phase;

        lock (_stateLock)
        {
            var state = GetOrCreateStateLocked(@event.Platform);
            state.IsStreamOnline = false;
            phase = state.Phase;
        }

        if (!_targetChannelProvider.Current.RecordsUserData)
        {
            _logger.LogDebug("Авто-отключение {Platform} пропущено – режим отладки без записи данных", @event.Platform);
            return Task.CompletedTask;
        }

        var settings = _settingsProvider.Get(@event.Platform);

        if (!settings.AutoDisconnectOnStreamOffline)
        {
            return Task.CompletedTask;
        }

        if (@event.IsCatchUp)
        {
            _logger.LogDebug(
                "Авто-отключение {Platform} пропущено: офлайн-снимок при подключении бота (catch-up), а не настоящий переход стрима в офлайн",
                @event.Platform);
            return Task.CompletedTask;
        }

        if (phase != BotLifecyclePhase.Connected)
        {
            _logger.LogDebug("Авто-отключение {Platform} пропущено: текущая фаза {Phase}", @event.Platform, phase);
            return Task.CompletedTask;
        }

        _logger.LogInformation("⚫ Стрим офлайн ({Platform}, {Channel}) – автоматически отключаю бота", @event.Platform, @event.Channel);

        var platform = @event.Platform;
        _offlineStopCoordinator.ScheduleOfflineStop(platform, _ => StopBotAsync(platform));

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _onlineSubscription.Dispose();
        _offlineSubscription.Dispose();
        _phaseSubscription.Dispose();
        _activeChannelSubscription.Dispose();
    }

    private PlatformState GetOrCreateStateLocked(StreamingPlatform platform)
    {
        if (!_states.TryGetValue(platform, out var state))
        {
            state = new PlatformState();
            _states[platform] = state;
        }

        return state;
    }

    private async Task StopBotAsync(StreamingPlatform platform)
    {
        lock (_stateLock)
        {
            if (_states.TryGetValue(platform, out var state) && state.IsStreamOnline)
            {
                _logger.LogInformation("Авто-отключение {Platform} отменено: стрим снова онлайн", platform);
                return;
            }
        }

        try
        {
            await _connectionManager.StopAsync(platform, BotStopMode.Graceful).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка авто-отключения бота для {Platform}", platform);
        }
    }

    private static bool IsDisconnectedPhase(BotLifecyclePhase phase)
    {
        return phase is BotLifecyclePhase.Idle
            or BotLifecyclePhase.Disconnected
            or BotLifecyclePhase.Cancelled
            or BotLifecyclePhase.Failed;
    }
}
