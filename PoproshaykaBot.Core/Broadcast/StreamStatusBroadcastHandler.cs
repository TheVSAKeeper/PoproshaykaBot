using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class StreamStatusBroadcastHandler :
    IEventHandler<BroadcastStarted>,
    IEventHandler<BroadcastPlatformJoined>,
    IEventHandler<BroadcastPlatformLeft>,
    IEventHandler<BroadcastEnded>,
    IEventHandler<PlatformConnectionChanged>,
    IEventHandler<PlatformChatAvailabilityChanged>,
    IEventSubscriber,
    IDisposable
{
    private readonly IBroadcastScheduler _scheduler;
    private readonly IBroadcastMessenger _messenger;
    private readonly IBroadcastState _broadcastState;
    private readonly IBroadcastPlatformTargetsProvider _targetsProvider;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<StreamStatusBroadcastHandler> _logger;
    private readonly IDisposable _startedSubscription;
    private readonly IDisposable _joinedSubscription;
    private readonly IDisposable _leftSubscription;
    private readonly IDisposable _endedSubscription;
    private readonly IDisposable _phaseSubscription;
    private readonly IDisposable _chatAvailabilitySubscription;
    private readonly Lock _stateLock = new();

    private readonly HashSet<StreamingPlatform> _connectedPlatforms = [];
    private readonly HashSet<StreamingPlatform> _pendingStartNotifications = [];
    private readonly Dictionary<StreamingPlatform, long> _activeStartSends = [];
    private readonly HashSet<StreamingPlatform> _retryRequestedDuringSend = [];
    private readonly Dictionary<StreamingPlatform, string> _platformChannels = [];
    private long _broadcastGeneration;
    private long _nextSendId;

    public StreamStatusBroadcastHandler(
        IBroadcastScheduler scheduler,
        IBroadcastMessenger messenger,
        IBroadcastState broadcastState,
        IBroadcastPlatformTargetsProvider targetsProvider,
        ITargetChannelProvider targetChannelProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<StreamStatusBroadcastHandler> logger,
        IChatPlatformRegistry? platformRegistry = null)
    {
        _scheduler = scheduler;
        _messenger = messenger;
        _broadcastState = broadcastState;
        _targetsProvider = targetsProvider;
        _platformRegistry = platformRegistry;
        _targetChannelProvider = targetChannelProvider;
        _settingsManager = settingsManager;
        _logger = logger;

        _startedSubscription = eventBus.Subscribe<BroadcastStarted>(this);
        _joinedSubscription = eventBus.Subscribe<BroadcastPlatformJoined>(this);
        _leftSubscription = eventBus.Subscribe<BroadcastPlatformLeft>(this);
        _endedSubscription = eventBus.Subscribe<BroadcastEnded>(this);
        _phaseSubscription = eventBus.Subscribe<PlatformConnectionChanged>(this);
        _chatAvailabilitySubscription = eventBus.Subscribe<PlatformChatAvailabilityChanged>(this);
    }

    public Task HandleAsync(PlatformConnectionChanged @event, CancellationToken cancellationToken)
    {
        var targets = _targetsProvider.GetTargetPlatforms();
        var connected = @event.Phase == BotLifecyclePhase.Connected;
        var shouldStartScheduler = false;
        var shouldStopScheduler = false;
        var sendDeferredStart = false;
        long generation = 0;
        long sendId = 0;
        string? channelToStart = null;

        lock (_stateLock)
        {
            if (!connected)
            {
                var wasConnected = _connectedPlatforms.Remove(@event.Platform);
                _retryRequestedDuringSend.Remove(@event.Platform);
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
                        if (_platformRegistry is null
                            || (_platformRegistry.TryGet(@event.Platform, out var adapter) && adapter.IsConnected))
                        {
                            sendDeferredStart = TryAcquireOrDeferStartSendLocked(@event.Platform, out generation, out sendId);
                        }

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

        try
        {
            if (shouldStartScheduler && channelToStart is not null)
            {
                StartSchedulerIfEnabled(channelToStart);
            }

            if (sendDeferredStart)
            {
                SendStartNotificationToPlatform(@event.Platform, generation, sendId);
            }
        }
        finally
        {
            if (sendDeferredStart)
            {
                ReleaseActiveStartSend(@event.Platform, sendId);
            }
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(PlatformChatAvailabilityChanged @event, CancellationToken cancellationToken)
    {
        if (!@event.IsAvailable)
        {
            return Task.CompletedTask;
        }

        var sendDeferredStart = false;
        long generation = 0;
        long sendId = 0;

        lock (_stateLock)
        {
            var isChatReady = _platformRegistry is null
                || (_platformRegistry.TryGet(@event.Platform, out var adapter) && adapter.IsConnected);
            if (_broadcastState.IsAnyLive && _connectedPlatforms.Contains(@event.Platform) && isChatReady)
            {
                sendDeferredStart = TryAcquireOrDeferStartSendLocked(@event.Platform, out generation, out sendId);
            }
        }

        if (sendDeferredStart)
        {
            SendStartNotificationToPlatform(@event.Platform, generation, sendId);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(BroadcastStarted @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обработка старта эфира {BroadcastId} для канала {Channel}", @event.BroadcastId, @event.Channel);

        var targets = _targetsProvider.GetTargetPlatforms();
        Dictionary<StreamingPlatform, long>? sendIds = null;
        long generation;
        string channelToStart;
        var shouldStartScheduler = false;

        lock (_stateLock)
        {
            _broadcastGeneration++;
            generation = _broadcastGeneration;
            _pendingStartNotifications.Clear();
            _activeStartSends.Clear();
            _retryRequestedDuringSend.Clear();
            _platformChannels[@event.FirstPlatform] = @event.Channel;

            if (!@event.IsCatchUp)
            {
                var knownPlatforms = _platformRegistry is not null
                    ? _platformRegistry.All.Select(static p => p.Id)
                    : [StreamingPlatform.Twitch, StreamingPlatform.YouTube];

                foreach (var target in knownPlatforms)
                {
                    if (!_connectedPlatforms.Contains(target))
                    {
                        _pendingStartNotifications.Add(target);
                    }
                    else
                    {
                        sendIds ??= [];
                        var sendId = ++_nextSendId;
                        sendIds[target] = sendId;
                        _activeStartSends[target] = sendId;
                    }
                }
            }

            if (_connectedPlatforms.Overlaps(targets) && !_scheduler.IsActive)
            {
                channelToStart = ResolveSchedulerChannelLocked();
                shouldStartScheduler = !string.IsNullOrWhiteSpace(channelToStart);
            }
            else
            {
                channelToStart = @event.Channel;
            }
        }

        try
        {
            if (shouldStartScheduler)
            {
                StartSchedulerIfEnabled(channelToStart);
            }

            if (sendIds is { Count: > 0 })
            {
                SendStartNotification(@event.Channel, sendIds, generation);
            }
        }
        finally
        {
            if (sendIds is { Count: > 0 })
            {
                ReleaseActiveStartSends(sendIds);
            }
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

        IReadOnlySet<StreamingPlatform> connectedTargets;

        lock (_stateLock)
        {
            _broadcastGeneration++;
            _pendingStartNotifications.Clear();
            _activeStartSends.Clear();
            _retryRequestedDuringSend.Clear();
            _platformChannels.Clear();

            connectedTargets = _connectedPlatforms.ToHashSet();
        }

        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (settings.AutoBroadcastEnabled && _scheduler.IsActive)
        {
            _logger.LogInformation("Остановка планировщика автоматической рассылки для эфира {BroadcastId}", @event.BroadcastId);
            _scheduler.Stop();
            _logger.LogInformation("⚫ Стрим офлайн. Автоматически останавливаю рассылку.");
        }

        if (connectedTargets.Count > 0
            && !@event.IsCatchUp
            && settings.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.StreamStopMessage))
        {
            _logger.LogDebug("Отправка уведомления об окончании эфира на подключённые площадки");
            _messenger.SendToConnected(settings.StreamStopMessage, ChatSendCategory.EventNotification);
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
        _chatAvailabilitySubscription.Dispose();
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

    private void SendStartNotification(string channel, Dictionary<StreamingPlatform, long> sendIds, long generation)
    {
        var settings = _settingsManager.Current.Twitch.AutoBroadcast;

        if (!settings.StreamStatusNotificationsEnabled
            || string.IsNullOrEmpty(settings.StreamStartMessage))
        {
            return;
        }

        _logger.LogDebug("Отправка уведомления о начале стрима в канал {Channel}", channel);
        var sendResult = _messenger.SendToConnected(settings.StreamStartMessage, ChatSendCategory.EventNotification);

        if (sendResult.PerPlatform is not { } perPlatform)
        {
            return;
        }

        List<(StreamingPlatform Platform, long SendId)>? platformsToRetry = null;

        lock (_stateLock)
        {
            foreach (var (platform, sendId) in sendIds)
            {
                var failedNotConnected = !perPlatform.TryGetValue(platform, out var enqueueResult)
                    || enqueueResult == ChatEnqueueResult.NotConnected;

                if (TryHandleSendCompletionLocked(platform, generation, sendId, failedNotConnected))
                {
                    platformsToRetry ??= [];
                    platformsToRetry.Add((platform, sendId));
                }
            }
        }

        if (platformsToRetry is null)
        {
            return;
        }

        foreach (var (platform, sendId) in platformsToRetry)
        {
            SendStartNotificationToPlatform(platform, generation, sendId);
        }
    }

    private void SendStartNotificationToPlatform(StreamingPlatform platform, long generation, long sendId)
    {
        try
        {
            while (true)
            {
                lock (_stateLock)
                {
                    if (!_broadcastState.IsAnyLive
                        || _broadcastGeneration != generation
                        || !_connectedPlatforms.Contains(platform)
                        || !_activeStartSends.TryGetValue(platform, out var currentSendId)
                        || currentSendId != sendId)
                    {
                        return;
                    }
                }

                var settings = _settingsManager.Current.Twitch.AutoBroadcast;

                if (!settings.StreamStatusNotificationsEnabled
                    || string.IsNullOrEmpty(settings.StreamStartMessage))
                {
                    return;
                }

                _logger.LogDebug("Отправка отложенного уведомления о начале стрима на площадку {Platform}", platform);
                var enqueueResult = _messenger.Send(platform, settings.StreamStartMessage, ChatSendCategory.EventNotification);

                lock (_stateLock)
                {
                    if (!TryHandleSendCompletionLocked(platform, generation, sendId, enqueueResult == ChatEnqueueResult.NotConnected))
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            ReleaseActiveStartSend(platform, sendId);
        }
    }

    private bool TryAcquireOrDeferStartSendLocked(
        StreamingPlatform platform,
        out long generation,
        out long sendId)
    {
        if (_activeStartSends.ContainsKey(platform))
        {
            _retryRequestedDuringSend.Add(platform);
            generation = 0;
            sendId = 0;
            return false;
        }

        if (_pendingStartNotifications.Remove(platform))
        {
            generation = _broadcastGeneration;
            sendId = ++_nextSendId;
            _activeStartSends[platform] = sendId;
            return true;
        }

        generation = 0;
        sendId = 0;
        return false;
    }

    private bool TryHandleSendCompletionLocked(
        StreamingPlatform platform,
        long generation,
        long sendId,
        bool failedNotConnected)
    {
        if (!_activeStartSends.TryGetValue(platform, out var currentSendId) || currentSendId != sendId)
        {
            return false;
        }

        if (!failedNotConnected
            || !_broadcastState.IsAnyLive
            || _broadcastGeneration != generation)
        {
            _activeStartSends.Remove(platform);
            _retryRequestedDuringSend.Remove(platform);
            return false;
        }

        if (_connectedPlatforms.Contains(platform) && _retryRequestedDuringSend.Remove(platform))
        {
            return true;
        }

        _activeStartSends.Remove(platform);
        _retryRequestedDuringSend.Remove(platform);
        _pendingStartNotifications.Add(platform);
        return false;
    }

    private void ReleaseActiveStartSend(StreamingPlatform platform, long sendId)
    {
        lock (_stateLock)
        {
            ReleaseActiveStartSendLocked(platform, sendId);
        }
    }

    private void ReleaseActiveStartSends(Dictionary<StreamingPlatform, long> sendIds)
    {
        lock (_stateLock)
        {
            foreach (var (platform, sendId) in sendIds)
            {
                ReleaseActiveStartSendLocked(platform, sendId);
            }
        }
    }

    private void ReleaseActiveStartSendLocked(StreamingPlatform platform, long sendId)
    {
        if (_activeStartSends.TryGetValue(platform, out var currentSendId) && currentSendId == sendId)
        {
            _activeStartSends.Remove(platform);
            _retryRequestedDuringSend.Remove(platform);
        }
    }
}
