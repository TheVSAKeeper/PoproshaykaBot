using Microsoft.Extensions.Logging;
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
    private readonly IEventBus _eventBus;
    private readonly ILogger<BotLifecycleAutomationHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly IDisposable _phaseSubscription;
    private readonly Lock _stateLock = new();
    private readonly Dictionary<StreamingPlatform, PlatformState> _states = [];

    public BotLifecycleAutomationHandler(
        IBotConnectionController connectionManager,
        IBotLifecycleAutomationSettingsProvider settingsProvider,
        IEventBus eventBus,
        ILogger<BotLifecycleAutomationHandler> logger)
    {
        _connectionManager = connectionManager;
        _settingsProvider = settingsProvider;
        _eventBus = eventBus;
        _logger = logger;

        _onlineSubscription = eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = eventBus.Subscribe<StreamWentOffline>(this);
        _phaseSubscription = eventBus.Subscribe<PlatformConnectionChanged>(this);
    }

    public Task HandleAsync(PlatformConnectionChanged @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            GetOrCreateStateLocked(@event.Platform).Phase = @event.Phase;
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

        var settings = _settingsProvider.Get(@event.Platform);

        if (!settings.AutoConnectOnStreamOnline)
        {
            return Task.CompletedTask;
        }

        if (!IsDisconnectedPhase(phase))
        {
            _logger.LogDebug("Авто-подключение {Platform} пропущено: текущая фаза {Phase}", @event.Platform, phase);
            return Task.CompletedTask;
        }

        if (_connectionManager.IsPlatformBusy(@event.Platform))
        {
            _logger.LogDebug("Авто-подключение {Platform} пропущено: BotConnectionManager занят", @event.Platform);
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogInformation("🔴 Стрим онлайн ({Platform}, {Channel}) – автоматически подключаю бота", @event.Platform, @event.Channel);
            _connectionManager.StartConnection(@event.Platform);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Не удалось запустить авто-подключение бота для {Platform}", @event.Platform);
        }

        return Task.CompletedTask;
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
        _eventBus.ContinueAfterPublish(() => StopBotAsync(platform));

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _onlineSubscription.Dispose();
        _offlineSubscription.Dispose();
        _phaseSubscription.Dispose();
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
