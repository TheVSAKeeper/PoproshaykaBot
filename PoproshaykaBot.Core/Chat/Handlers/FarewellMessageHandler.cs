using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class FarewellMessageHandler : IEventHandler<BotLifecyclePhaseChanged>, IEventSubscriber, IDisposable
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly AudienceTracker _audienceTracker;
    private readonly IBroadcastMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<FarewellMessageHandler> _logger;
    private readonly IDisposable _subscription;

    public FarewellMessageHandler(
        IChatPlatformRegistry platformRegistry,
        AudienceTracker audienceTracker,
        IBroadcastMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<FarewellMessageHandler> logger)
    {
        _platformRegistry = platformRegistry;
        _audienceTracker = audienceTracker;
        _messenger = messenger;
        _settingsManager = settingsManager;
        _logger = logger;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(BotLifecyclePhaseChanged @event, CancellationToken cancellationToken)
    {
        if (@event.Phase != BotLifecyclePhase.Disconnecting)
        {
            return Task.CompletedTask;
        }

        var connected = _platformRegistry.GetConnected();

        if (connected.Count == 0)
        {
            _logger.LogDebug("Прощальные сообщения пропущены: бот не заходил в канал в этой сессии");
            return Task.CompletedTask;
        }

        var messageSettings = _settingsManager.Current.Twitch.Messages;

        if (!messageSettings.FarewellEnabled)
        {
            _logger.LogInformation("Коллективное прощание выключено настройкой Messages.FarewellEnabled");
        }

        foreach (var platform in connected)
        {
            var audience = _audienceTracker.ForPlatform(platform.Id);
            var activeUsers = audience.ActiveUserCount;
            var messages = new List<string>();

            if (messageSettings.FarewellEnabled)
            {
                if (activeUsers == 0)
                {
                    _logger.LogInformation("Коллективное прощание для {Platform} пропущено: AudienceTracker пуст – ни одного нефильтрованного сообщения за сессию",
                        platform.Id);
                }
                else
                {
                    var collectiveFarewell = audience.CreateCollectiveFarewell();

                    if (!string.IsNullOrWhiteSpace(collectiveFarewell))
                    {
                        messages.Add(collectiveFarewell);
                        _logger.LogInformation("Сформировано коллективное прощание для {Platform} для {Count} зрителей",
                            platform.Id,
                            activeUsers);
                    }
                }
            }

            if (messageSettings.DisconnectionEnabled
                && !string.IsNullOrWhiteSpace(messageSettings.Disconnection))
            {
                messages.Add(messageSettings.Disconnection);
            }

            if (messages.Count == 0)
            {
                _logger.LogDebug("На фазе Disconnecting не сформировано ни одного прощального сообщения для {Platform}", platform.Id);
                continue;
            }

            var text = string.Join(" ", messages);

            try
            {
                var result = _messenger.Send(platform.Id, text);

                if (result is ChatEnqueueResult.Queued)
                {
                    _logger.LogInformation("Прощальное сообщение для {Platform} поставлено в очередь ({Length} симв.)",
                        platform.Id,
                        text.Length);
                }
                else
                {
                    _logger.LogDebug("Прощальное сообщение для {Platform} не поставлено в очередь ChatSender: {Reason}",
                        platform.Id,
                        ChatEnqueueResults.DescribeRefusal(result));
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Не удалось поставить прощальное сообщение для {Platform} в очередь", platform.Id);
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
