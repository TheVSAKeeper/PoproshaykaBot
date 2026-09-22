using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.Core.Twitch.Chat;

public sealed class ChatIngestionService(
    [FromKeyedServices(TwitchEndpoints.EventSubBotSession)]
    ITwitchEventSubClient eventSubClient,
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    IBotUserIdProvider botUserIdProvider,
    SettingsManager settingsManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<ChatIngestionService> logger)
    : IHostedComponent
{
    private readonly object _joinLock = new();
    private bool _subscribed;
    private long _generation;
    private long _joinedAtUtcTicks;
    private long _lastMessageAtUtcTicks;

    public string Name => "Чтение сообщений чата (EventSub)";

    public int StartOrder => 250;

    public bool IsJoined => Interlocked.Read(ref _joinedAtUtcTicks) != 0;

    public DateTimeOffset? JoinedAt => ToTimestamp(Interlocked.Read(ref _joinedAtUtcTicks));

    public DateTimeOffset? LastMessageAt => ToTimestamp(Interlocked.Read(ref _lastMessageAtUtcTicks));

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return Task.CompletedTask;
        }

        eventSubClient.OnSessionWelcome += HandleSessionWelcomeAsync;
        eventSubClient.OnNotification += HandleNotificationAsync;
        eventSubClient.OnDisconnected += HandleDisconnectedAsync;
        eventSubClient.OnRevocation += HandleRevocationAsync;
        _subscribed = true;

        logger.LogInformation("ChatIngestionService: подписка на EventSub установлена");

        if (eventSubClient.SessionId is { } sessionId)
        {
            return HandleSessionWelcomeAsync(new(sessionId, null), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            return;
        }

        eventSubClient.OnSessionWelcome -= HandleSessionWelcomeAsync;
        eventSubClient.OnNotification -= HandleNotificationAsync;
        eventSubClient.OnDisconnected -= HandleDisconnectedAsync;
        eventSubClient.OnRevocation -= HandleRevocationAsync;
        _subscribed = false;

        logger.LogInformation("ChatIngestionService: отписка от EventSub");

        await MarkLeftAsync();
    }

    private static DateTimeOffset? ToTimestamp(long utcTicks)
    {
        return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }

    private async Task HandleSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken ct)
    {
        logger.LogInformation("ChatIngestionService: EventSub сессия открыта ({SessionId}), регистрируем channel.chat.message", args.SessionId);

        await MarkLeftAsync(out var generation);

        try
        {
            var settings = settingsManager.Current.Twitch;

            var broadcasterId = await broadcasterIdProvider.GetAsync(ct);
            if (string.IsNullOrEmpty(broadcasterId))
            {
                logger.LogError("ChatIngestionService: не удалось получить broadcaster id для канала '{Channel}'", settings.Channel);
                return;
            }

            var botId = await botUserIdProvider.GetAsync(ct);
            if (string.IsNullOrEmpty(botId))
            {
                logger.LogError("ChatIngestionService: не удалось получить user id бота через токен");
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
                            ["broadcaster_user_id"] = broadcasterId,
                            ["user_id"] = botId,
                        },
                        args.SessionId,
                        ct);

                    await MarkJoinedAsync(generation);

                    logger.LogInformation("ChatIngestionService: подписка channel.chat.message создана (broadcaster={BroadcasterId}, bot={BotId}, попытка {Attempt})",
                        broadcasterId, botId, attempt + 1);

                    return;
                }
                catch (HelixRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    await MarkJoinedAsync(generation);

                    logger.LogInformation(ex, "ChatIngestionService: подписка channel.chat.message уже существует для текущей EventSub-сессии – переиспользуем (broadcaster={BroadcasterId}, bot={BotId})",
                        broadcasterId, botId);

                    return;
                }
                catch (Exception ex) when (attempt < delays.Length - 1)
                {
                    lastError = ex;
                    logger.LogWarning(ex, "ChatIngestionService: попытка {Attempt}/{Max} создать подписку channel.chat.message не удалась", attempt + 1, delays.Length);
                }
            }

            logger.LogError(lastError, "ChatIngestionService: не удалось подписаться на чат Twitch – все {Max} попытки создать подписку channel.chat.message провалились", delays.Length);
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

        Interlocked.Exchange(ref _lastMessageAtUtcTicks, timeProvider.GetUtcNow().UtcTicks);

        try
        {
            var botId = await botUserIdProvider.GetAsync(ct);
            var chatMessage = EventSubChatMessageMapper.Map(args.Payload, botId);
            var timestamp = new DateTimeOffset(args.MessageTimestamp, TimeSpan.Zero);
            await eventBus.PublishAsync(new RawChatMessageReceived(chatMessage, timestamp), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChatIngestionService: ошибка обработки channel.chat.message");
        }
    }

    private Task HandleDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken ct)
    {
        return MarkLeftAsync();
    }

    private Task HandleRevocationAsync(EventSubRevocationArgs args, CancellationToken ct)
    {
        if (!string.Equals(args.SubscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        logger.LogWarning("ChatIngestionService: Twitch отозвал подписку channel.chat.message ({Status}) – сообщения чата больше не приходят", args.Status);

        return MarkLeftAsync();
    }

    private Task MarkJoinedAsync(long generation)
    {
        var now = timeProvider.GetUtcNow();

        lock (_joinLock)
        {
            if (generation != _generation || Interlocked.Exchange(ref _joinedAtUtcTicks, now.UtcTicks) != 0)
            {
                return Task.CompletedTask;
            }
        }

        return eventBus.PublishAsync(new ChatIngestionStarted(now), CancellationToken.None);
    }

    private Task MarkLeftAsync()
    {
        return MarkLeftAsync(out _);
    }

    private Task MarkLeftAsync(out long generation)
    {
        long previous;

        lock (_joinLock)
        {
            generation = ++_generation;
            previous = Interlocked.Exchange(ref _joinedAtUtcTicks, 0);
        }

        return previous != 0
            ? eventBus.PublishAsync(new ChatIngestionStopped(timeProvider.GetUtcNow()), CancellationToken.None)
            : Task.CompletedTask;
    }
}
