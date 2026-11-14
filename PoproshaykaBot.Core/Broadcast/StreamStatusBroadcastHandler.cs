using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class StreamStatusBroadcastHandler :
    IEventHandler<BroadcastStarted>,
    IEventHandler<BroadcastPlatformJoined>,
    IEventHandler<BroadcastPlatformLeft>,
    IEventHandler<BroadcastEnded>,
    IEventHandler<PlatformConnectionChanged>,
    IEventSubscriber,
    IDisposable
{
    private readonly IBroadcastScheduler _scheduler;
    private readonly IBroadcastMessenger _messenger;
    private readonly IBroadcastState _broadcastState;
    private readonly IBroadcastPlatformTargetsProvider _targetsProvider;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<StreamStatusBroadcastHandler> _logger;
    private readonly IDisposable _startedSubscription;
    private readonly IDisposable _joinedSubscription;
    private readonly IDisposable _leftSubscription;
    private readonly IDisposable _endedSubscription;
    private readonly IDisposable _phaseSubscription;
    private readonly Lock _stateLock = new();

    private readonly HashSet<StreamingPlatform> _connectedPlatforms = [];
    private readonly HashSet<StreamingPlatform> _pendingStartNotifications = [];
    private readonly Dictionary<StreamingPlatform, string> _platformChannels = [];

    public StreamStatusBroadcastHandler(
        IBroadcastScheduler scheduler,
        IBroadcastMessenger messenger,
        IBroadcastState broadcastState,
        IBroadcastPlatformTargetsProvider targetsProvider,
        ITargetChannelProvider targetChannelProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<StreamStatusBroadcastHandler> logger)
    {
        _scheduler = scheduler;
        _messenger = messenger;
        _broadcastState = broadcastState;
        _targetsProvider = targetsProvider;
        _targetChannelProvider = targetChannelProvider;
        _settingsManager = settingsManager;
        _logger = logger;

        _startedSubscription = eventBus.Subscribe<BroadcastStarted>(this);
        _joinedSubscription = eventBus.Subscribe<BroadcastPlatformJoined>(this);
        _leftSubscription = eventBus.Subscribe<BroadcastPlatformLeft>(this);
        _endedSubscription = eventBus.Subscribe<BroadcastEnded>(this);
        _phaseSubscription = eventBus.Subscribe<PlatformConnectionChanged>(this);
    }

    public Task HandleAsync(PlatformConnectionChanged @event, CancellationToken cancellationToken)
    {
        var targets = _targetsProvider.GetTargetPlatforms();
        var connected = @event.Phase == BotLifecyclePhase.Connected;
        var shouldStartScheduler = false;
        var shouldStopScheduler = false;
        var sendDeferredStart = false;
        string? channelToStart = null;

        lock (_stateLock)
        {
            if (!connected)
            {
                var wasConnected = _connectedPlatforms.Remove(@event.Platform);
                if (wasConnected && _scheduler.IsActive && !_connectedPlatforms.Overlaps(targets))
                {
                    shouldStopScheduler = true;
                }
            }
            else
            {
                var newlyConnected = _connectedPlatforms.Add(@event.Platform);
                if (newlyConnected)
                {
                    if (!_broadcastState.IsAnyLive)
                    {
                        _pendingStartNotifications.Remove(@event.Platform);
                    }
                    else
                    {
                        sendDeferredStart = _pendingStartNotifications.Remove(@event.Platform);

                        if (targets.Contains(@event.Platform) && !_scheduler.IsActive)
                        {
                            channelToStart = ResolveSchedulerChannelLocked();
                            shouldStartScheduler = !string.IsNullOrWhiteSpace(channelToStart);
                        }
                    }
                }
            }
        }

        if (shouldStopScheduler)
        {
            _logger.LogInformation("Остановка планировщика автоматической рассылки при отключении площадки {Platform}", @event.Platform);
            _scheduler.Stop();
            return Task.CompletedTask;
        }

        if (shouldStartScheduler && channelToStart is not null)
        {
            StartSchedulerIfEnabled(channelToStart);
        }

        if (sendDeferredStart && targets.Contains(@event.Platform))
        {
            SendStartNotificationToPlatform(@event.Platform);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastStarted @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обработка старта эфира {BroadcastId} для канала {Channel}", @event.BroadcastId, @event.Channel);

        var targets = _targetsProvider.GetTargetPlatforms();
        IReadOnlySet<StreamingPlatform> connectedTargets;
        string channelToStart;
        var shouldStartScheduler = false;

        lock (_stateLock)
        {
            _platformChannels[@event.FirstPlatform] = @event.Channel;

            connectedTargets = _connectedPlatforms
                .Where(targets.Contains)
                .ToHashSet();

            if (!@event.IsCatchUp)
            {
                foreach (var target in targets)
                {
                    if (!_connectedPlatforms.Contains(target))
                    {
                        _pendingStartNotifications.Add(target);
                    }
                }
            }

            if (connectedTargets.Count > 0 && !_scheduler.IsActive)
            {
                channelToStart = ResolveSchedulerChannelLocked();
                shouldStartScheduler = !string.IsNullOrWhiteSpace(channelToStart);
            }
            else
            {
                channelToStart = @event.Channel;
            }
        }

        if (shouldStartScheduler)
        {
            StartSchedulerIfEnabled(channelToStart);
        }

        if (!@event.IsCatchUp && connectedTargets.Count > 0)
        {
            SendStartNotification(@event.Channel);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastPlatformJoined @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _platformChannels[@event.Platform] = @event.Channel;
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastPlatformLeft @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _platformChannels.Remove(@event.Platform);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastEnded @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обработка окончания эфира {BroadcastId} для канала {Channel}", @event.BroadcastId, @event.LastChannel);

        var targets = _targetsProvider.GetTargetPlatforms();
        IReadOnlySet<StreamingPlatform> connectedTargets;

        lock (_stateLock)
        {
            _pendingStartNotifications.Clear();
            _platformChannels.Clear();

            connectedTargets = _connectedPlatforms
                .Where(targets.Contains)
                .ToHashSet();
        }

        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (settings.AutoBroadcastEnabled && _scheduler.IsActive)
        {
            _logger.LogInformation("Остановка планировщика автоматической рассылки для эфира {BroadcastId}", @event.BroadcastId);
            _scheduler.Stop();
            _logger.LogInformation("⚫ Стрим офлайн. Автоматически останавливаю рассылку.");
        }

        if (connectedTargets.Count > 0
            && settings.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.StreamStopMessage))
        {
            _logger.LogDebug("Отправка уведомления об окончании эфира на подключённые площадки");
            _messenger.SendToConnected(settings.StreamStopMessage);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _startedSubscription.Dispose();
        _joinedSubscription.Dispose();
        _leftSubscription.Dispose();
        _endedSubscription.Dispose();
        _phaseSubscription.Dispose();
    }

    private string ResolveSchedulerChannelLocked()
    {
        if (_platformChannels.TryGetValue(StreamingPlatform.Twitch, out var twitchChannel)
            && !string.IsNullOrWhiteSpace(twitchChannel))
        {
            return twitchChannel;
        }

        foreach (var channel in _platformChannels.Values)
        {
            if (!string.IsNullOrWhiteSpace(channel))
            {
                return channel;
            }
        }

        return _targetChannelProvider.Current.Login;
    }

    private void StartSchedulerIfEnabled(string channel)
    {
        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (settings.AutoBroadcastEnabled && !_scheduler.IsActive)
        {
            _logger.LogInformation("Запуск планировщика автоматической рассылки для канала {Channel}", channel);
            _scheduler.Start(channel);
            _logger.LogInformation("🔴 Стрим онлайн. Автоматически запускаю рассылку.");
        }
    }

    private void SendStartNotification(string channel)
    {
        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (settings.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.StreamStartMessage))
        {
            _logger.LogDebug("Отправка уведомления о начале стрима в канал {Channel}", channel);
            _messenger.SendToConnected(settings.StreamStartMessage);
        }
    }

    private void SendStartNotificationToPlatform(StreamingPlatform platform)
    {
        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (settings.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.StreamStartMessage))
        {
            _logger.LogDebug("Отправка отложенного уведомления о начале стрима на площадку {Platform}", platform);
            _messenger.Send(platform, settings.StreamStartMessage);
        }
    }
}
