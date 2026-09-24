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
    private const string ChatMessageSubscriptionType = "channel.chat.message";

    private readonly object _joinLock = new();
    private bool _subscribed;
    private long _generation;
    private string? _subscriptionId;
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

        await MarkLeftAsync(out _, out var subscriptionId);

        if (subscriptionId != null)
        {
            await DeleteSubscriptionAsync(subscriptionId, cancellationToken);
        }
    }

    private static DateTimeOffset? ToTimestamp(long utcTicks)
    {
        return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }

    private async Task HandleSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken ct)
    {
        logger.LogInformation("ChatIngestionService: EventSub сессия открыта ({SessionId}), регистрируем channel.chat.message", args.SessionId);

        await MarkLeftAsync(out var generation, out _);

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
                    var subscriptionId = await helix.CreateEventSubSubscriptionAsync("channel.chat.message",
                        "1",
                        new Dictionary<string, string>
                        {
                            ["broadcaster_user_id"] = broadcasterId,
                            ["user_id"] = botId,
                        },
                        args.SessionId,
                        ct);

                    await MarkJoinedAsync(generation, subscriptionId, out var current);

                    if (!current)
                    {
                        logger.LogInformation("ChatIngestionService: подписка channel.chat.message {SubscriptionId} создана, когда приём чата уже остановлен – удаляем",
                            subscriptionId);

                        await DeleteSubscriptionAsync(subscriptionId, ct);
                        return;
                    }

                    logger.LogInformation("ChatIngestionService: подписка channel.chat.message создана (broadcaster={BroadcasterId}, bot={BotId}, попытка {Attempt})",
                        broadcasterId, botId, attempt + 1);

                    return;
                }
                catch (HelixRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    var existingId = await FindExistingSubscriptionAsync(args.SessionId, broadcasterId, botId, ct);

                    await MarkJoinedAsync(generation, existingId, out var current);

                    if (!current)
                    {
                        if (existingId != null)
                        {
                            logger.LogInformation("ChatIngestionService: существующая подписка channel.chat.message {SubscriptionId} найдена, когда приём чата уже остановлен – удаляем",
                                existingId);

                            await DeleteSubscriptionAsync(existingId, ct);
                        }

                        return;
                    }

                    if (existingId != null)
                    {
                        logger.LogInformation("ChatIngestionService: подписка channel.chat.message {SubscriptionId} уже существует для текущей EventSub-сессии – переиспользуем (broadcaster={BroadcasterId}, bot={BotId})",
                            existingId, broadcasterId, botId);
                    }
                    else
                    {
                        logger.LogWarning("ChatIngestionService: подписка channel.chat.message уже существует для текущей EventSub-сессии, но её id не найден – переиспользуем, при остановке она не удаляется (broadcaster={BroadcasterId}, bot={BotId})",
                            broadcasterId, botId);
                    }

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

    private async Task<string?> FindExistingSubscriptionAsync(string sessionId, string broadcasterId, string botId, CancellationToken ct)
    {
        try
        {
            var subscriptions = await helix.GetEventSubSubscriptionsAsync(ChatMessageSubscriptionType, ct);

            var matches = subscriptions
                .Where(x => string.Equals(x.Type, ChatMessageSubscriptionType, StringComparison.Ordinal)
                            && string.Equals(x.SessionId, sessionId, StringComparison.Ordinal)
                            && HasCondition(x, "broadcaster_user_id", broadcasterId)
                            && HasCondition(x, "user_id", botId))
                .ToList();

            if (matches.Count > 1)
            {
                logger.LogWarning("ChatIngestionService: для EventSub-сессии {SessionId} найдено {Count} подписок channel.chat.message – при остановке удаляется только {SubscriptionId}",
                    sessionId, matches.Count, matches[0].Id);
            }

            return matches.FirstOrDefault()?.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ChatIngestionService: не удалось найти существующую подписку channel.chat.message для EventSub-сессии {SessionId}", sessionId);
            return null;
        }

        static bool HasCondition(EventSubSubscriptionInfo subscription, string key, string value)
        {
            return subscription.Condition.TryGetValue(key, out var actual)
                   && string.Equals(actual, value, StringComparison.Ordinal);
        }
    }

    private async Task DeleteSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        try
        {
            if (!await helix.DeleteEventSubSubscriptionAsync(subscriptionId, ct))
            {
                logger.LogDebug("ChatIngestionService: подписки channel.chat.message {SubscriptionId} в Twitch уже нет", subscriptionId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ChatIngestionService: не удалось удалить подписку channel.chat.message {SubscriptionId} – уведомления чата будут приходить, пока открыт сокет EventSub",
                subscriptionId);
        }
    }

    private Task MarkJoinedAsync(long generation, string? subscriptionId, out bool current)
    {
        var now = timeProvider.GetUtcNow();

        lock (_joinLock)
        {
            current = generation == _generation;
            if (!current)
            {
                return Task.CompletedTask;
            }

            _subscriptionId = subscriptionId;

            if (Interlocked.Exchange(ref _joinedAtUtcTicks, now.UtcTicks) != 0)
            {
                return Task.CompletedTask;
            }
        }

        return eventBus.PublishAsync(new ChatIngestionStarted(now), CancellationToken.None);
    }

    private Task MarkLeftAsync()
    {
        return MarkLeftAsync(out _, out _);
    }

    private Task MarkLeftAsync(out long generation, out string? subscriptionId)
    {
        long previous;

        lock (_joinLock)
        {
            generation = ++_generation;
            subscriptionId = _subscriptionId;
            _subscriptionId = null;
            previous = Interlocked.Exchange(ref _joinedAtUtcTicks, 0);
        }

        return previous != 0
            ? eventBus.PublishAsync(new ChatIngestionStopped(timeProvider.GetUtcNow()), CancellationToken.None)
            : Task.CompletedTask;
    }
}
