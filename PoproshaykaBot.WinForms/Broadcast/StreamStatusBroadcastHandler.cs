using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Broadcast;

public sealed class StreamStatusBroadcastHandler :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventHandler<BotLifecyclePhaseChanged>,
    IEventSubscriber,
    IDisposable
{
    private readonly IBroadcastScheduler _scheduler;
    private readonly IChatMessenger _messenger;
    private readonly IStreamStatus _streamStatus;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StreamStatusBroadcastHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;
    private readonly IDisposable _phaseSubscription;

    private bool _botConnected;

    public StreamStatusBroadcastHandler(
        IBroadcastScheduler scheduler,
        IChatMessenger messenger,
        IStreamStatus streamStatus,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<StreamStatusBroadcastHandler> logger)
    {
        _scheduler = scheduler;
        _messenger = messenger;
        _streamStatus = streamStatus;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _logger = logger;

        _onlineSubscription = _eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = _eventBus.Subscribe<StreamWentOffline>(this);
        _phaseSubscription = _eventBus.Subscribe<BotLifecyclePhaseChanged>(this);
    }

    public Task HandleAsync(BotLifecyclePhaseChanged @event, CancellationToken cancellationToken)
    {
        var wasConnected = _botConnected;
        _botConnected = @event.Phase == BotLifecyclePhase.Connected;

        if (wasConnected || !_botConnected || _streamStatus.CurrentStatus != StreamStatus.Online)
        {
            return Task.CompletedTask;
        }

        var channel = _settingsManager.Current.Twitch.Channel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return Task.CompletedTask;
        }

        return StartBroadcastAsync(channel, cancellationToken);
    }

    public Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обработка события стрима ONLINE для канала {Channel}", @event.Channel);

        if (!_botConnected)
        {
            _logger.LogDebug("Бот не подключён — рассылка не запускается, уведомление не отправляется");
            return Task.CompletedTask;
        }

        return StartBroadcastAsync(@event.Channel, cancellationToken);
    }

    public async Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Current.Twitch;
        var channel = @event.Channel;

        _logger.LogInformation("Обработка события стрима OFFLINE для канала {Channel}", channel);

        if (!_botConnected)
        {
            _logger.LogDebug("Бот не подключён — обработка офлайн пропущена");
            return;
        }

        if (settings.AutoBroadcast.AutoBroadcastEnabled && _scheduler.IsActive)
        {
            _logger.LogInformation("Остановка планировщика автоматической рассылки для канала {Channel}", channel);
            _scheduler.Stop();

            await _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Information, nameof(StreamStatusBroadcastHandler),
                        "⚫ Стрим офлайн. Автоматически останавливаю рассылку."),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (settings.AutoBroadcast.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.AutoBroadcast.StreamStopMessage))
        {
            _logger.LogDebug("Отправка уведомления об окончании стрима в канал {Channel}", channel);
            _messenger.Send(settings.AutoBroadcast.StreamStopMessage);
        }
    }

    public void Dispose()
    {
        _onlineSubscription.Dispose();
        _offlineSubscription.Dispose();
        _phaseSubscription.Dispose();
    }

    private async Task StartBroadcastAsync(string channel, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Current.Twitch;

        if (settings.AutoBroadcast.AutoBroadcastEnabled && !_scheduler.IsActive)
        {
            _logger.LogInformation("Запуск планировщика автоматической рассылки для канала {Channel}", channel);
            _scheduler.Start(channel);

            await _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Information, nameof(StreamStatusBroadcastHandler),
                        "🔴 Стрим онлайн. Автоматически запускаю рассылку."),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (settings.AutoBroadcast.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.AutoBroadcast.StreamStartMessage))
        {
            _logger.LogDebug("Отправка уведомления о начале стрима в канал {Channel}", channel);
            _messenger.Send(settings.AutoBroadcast.StreamStartMessage);
        }
    }
}
