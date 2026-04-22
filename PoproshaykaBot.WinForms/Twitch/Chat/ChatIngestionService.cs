using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Twitch.Chat;

public sealed class ChatIngestionService(
    ITwitchEventSubClient eventSubClient,
    ITwitchHelixClient helix,
    EventSubChatMessageMapper mapper,
    SettingsManager settingsManager,
    IEventBus eventBus,
    ILogger<ChatIngestionService> logger)
    : IHostedComponent
{
    private bool _subscribed;

    public string Name => "Чтение сообщений чата (EventSub)";

    public int StartOrder => 250;

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return Task.CompletedTask;
        }

        eventSubClient.OnSessionWelcome += HandleSessionWelcomeAsync;
        eventSubClient.OnNotification += HandleNotificationAsync;
        _subscribed = true;

        logger.LogInformation("ChatIngestionService: подписка на EventSub установлена");
        return Task.CompletedTask;
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            return Task.CompletedTask;
        }

        eventSubClient.OnSessionWelcome -= HandleSessionWelcomeAsync;
        eventSubClient.OnNotification -= HandleNotificationAsync;
        _subscribed = false;

        logger.LogInformation("ChatIngestionService: отписка от EventSub");
        return Task.CompletedTask;
    }

    private async Task HandleSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken ct)
    {
        logger.LogInformation("ChatIngestionService: EventSub сессия открыта ({SessionId}), регистрируем channel.chat.message", args.SessionId);

        try
        {
            var settings = settingsManager.Current.Twitch;

            var broadcaster = await helix.GetUserByLoginAsync(settings.Channel, ct);
            if (broadcaster == null)
            {
                logger.LogError("ChatIngestionService: не удалось получить broadcaster id для канала '{Channel}'", settings.Channel);
                return;
            }

            var bot = await helix.GetUserByLoginAsync(settings.BotUsername, ct);
            if (bot == null)
            {
                logger.LogError("ChatIngestionService: не удалось получить user id для бота '{BotUsername}'", settings.BotUsername);
                return;
            }

            var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };
            Exception? lastError = null;

            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                if (delays[attempt] > TimeSpan.Zero)
                {
                    await Task.Delay(delays[attempt], ct);
                }

                try
                {
                    await helix.CreateEventSubSubscriptionAsync("channel.chat.message",
                        "1",
                        new Dictionary<string, string>
                        {
                            ["broadcaster_user_id"] = broadcaster.Id,
                            ["user_id"] = bot.Id,
                        },
                        args.SessionId,
                        ct);

                    logger.LogInformation("ChatIngestionService: подписка channel.chat.message создана (broadcaster={BroadcasterId}, bot={BotId}, попытка {Attempt})",
                        broadcaster.Id, bot.Id, attempt + 1);

                    return;
                }
                catch (Exception ex) when (attempt < delays.Length - 1)
                {
                    lastError = ex;
                    logger.LogWarning(ex, "ChatIngestionService: попытка {Attempt}/{Max} создать подписку channel.chat.message не удалась", attempt + 1, delays.Length);
                }
            }

            logger.LogError(lastError, "ChatIngestionService: все {Max} попытки создать подписку channel.chat.message провалились", delays.Length);
            await eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Error, "ChatIngestion", "Не удалось подписаться на чат Twitch"), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChatIngestionService: ошибка создания EventSub подписки channel.chat.message");
        }
    }

    private async Task HandleNotificationAsync(EventSubNotificationArgs args, CancellationToken ct)
    {
        if (!string.Equals(args.SubscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var chatMessage = mapper.Map(args.Payload);
            var timestamp = new DateTimeOffset(args.MessageTimestamp, TimeSpan.Zero);
            await eventBus.PublishAsync(new RawChatMessageReceived(chatMessage, timestamp), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChatIngestionService: ошибка обработки channel.chat.message");
        }
    }
}
