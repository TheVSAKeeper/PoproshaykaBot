using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast;

public sealed class StreamStatusBroadcastHandler :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventSubscriber,
    IDisposable
{
    private readonly BroadcastScheduler _scheduler;
    private readonly TwitchChatMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StreamStatusBroadcastHandler> _logger;
    private readonly IDisposable _onlineSubscription;
    private readonly IDisposable _offlineSubscription;

    public StreamStatusBroadcastHandler(
        BroadcastScheduler scheduler,
        TwitchChatMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<StreamStatusBroadcastHandler> logger)
    {
        _scheduler = scheduler;
        _messenger = messenger;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _logger = logger;

        _onlineSubscription = _eventBus.Subscribe<StreamWentOnline>(this);
        _offlineSubscription = _eventBus.Subscribe<StreamWentOffline>(this);
    }

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Current.Twitch;
        var channel = @event.Channel;

        _logger.LogInformation("Обработка события стрима ONLINE для канала {Channel}", channel);

        if (!settings.AutoBroadcast.AutoBroadcastEnabled)
        {
            _logger.LogDebug("Автотрансляция отключена в настройках — пропуск запуска планировщика");
            return;
        }

        if (_scheduler.IsActive)
        {
            _logger.LogDebug("Планировщик уже активен для канала {Channel} — запуск пропущен", channel);
            return;
        }

        _logger.LogInformation("Запуск планировщика автоматической рассылки для канала {Channel}", channel);
        _scheduler.Start(channel);

        await _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Information, nameof(StreamStatusBroadcastHandler),
                    "🔴 Стрим онлайн. Автоматически запускаю рассылку."),
                cancellationToken)
            .ConfigureAwait(false);

        if (settings.AutoBroadcast.StreamStatusNotificationsEnabled
            && !string.IsNullOrEmpty(settings.AutoBroadcast.StreamStartMessage))
        {
            _logger.LogDebug("Отправка уведомления о начале стрима в канал {Channel}", channel);
            _messenger.Send(settings.AutoBroadcast.StreamStartMessage);
        }
    }

    public async Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Current.Twitch;
        var channel = @event.Channel;

        _logger.LogInformation("Обработка события стрима OFFLINE для канала {Channel}", channel);

        if (!settings.AutoBroadcast.AutoBroadcastEnabled || !_scheduler.IsActive)
        {
            return;
        }

        _logger.LogInformation("Остановка планировщика автоматической рассылки для канала {Channel}", channel);
        _scheduler.Stop();

        await _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Information, nameof(StreamStatusBroadcastHandler),
                    "⚫ Стрим офлайн. Автоматически останавливаю рассылку."),
                cancellationToken)
            .ConfigureAwait(false);

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
    }
}
