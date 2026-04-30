using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Streaming;

public sealed class ChannelUpdateSubscriber(
    ITwitchEventSubClient eventSubClient,
    [FromKeyedServices(TwitchEndpoints.HelixBroadcasterClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    IEventBus eventBus,
    ILogger<ChannelUpdateSubscriber> logger)
    : IHostedComponent
{
    private const string SubscriptionType = "channel.update";
    private const string SubscriptionVersion = "2";

    private bool _subscribed;

    public bool IsHealthy { get; private set; } = true;

    public string Name => "Подписка на EventSub channel.update";

    public int StartOrder => 255;

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return Task.CompletedTask;
        }

        eventSubClient.OnSessionWelcome += HandleSessionWelcomeAsync;
        eventSubClient.OnNotification += HandleNotificationAsync;
        eventSubClient.OnRevocation += HandleRevocationAsync;
        _subscribed = true;

        logger.LogInformation("ChannelUpdateSubscriber: хуки EventSub установлены");
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
        eventSubClient.OnRevocation -= HandleRevocationAsync;
        _subscribed = false;

        return Task.CompletedTask;
    }

    private async Task HandleSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        var broadcasterId = await broadcasterIdProvider.GetAsync(cancellationToken);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            logger.LogError("ChannelUpdateSubscriber: не удалось получить broadcaster id");
            IsHealthy = false;
            PublishHealthError("не удалось определить broadcaster id для подписки channel.update");
            return;
        }

        try
        {
            await helix.CreateEventSubSubscriptionAsync(SubscriptionType,
                SubscriptionVersion,
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                },
                args.SessionId,
                cancellationToken);

            IsHealthy = true;
            logger.LogInformation("ChannelUpdateSubscriber: подписка на {Type} создана", SubscriptionType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChannelUpdateSubscriber: ошибка подписки на {Type}", SubscriptionType);
            IsHealthy = false;
            PublishHealthError($"не удалось подписаться на {SubscriptionType}: смена title/game работать не будет");
        }
    }

    private async Task HandleNotificationAsync(EventSubNotificationArgs args, CancellationToken cancellationToken)
    {
        if (!string.Equals(args.SubscriptionType, SubscriptionType, StringComparison.Ordinal))
        {
            return;
        }

        if (!args.Payload.TryGetProperty("event", out var evt))
        {
            return;
        }

        var title = evt.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
        var language = evt.TryGetProperty("language", out var langProp) ? langProp.GetString() ?? string.Empty : string.Empty;
        var gameId = evt.TryGetProperty("category_id", out var gameIdProp) ? gameIdProp.GetString() ?? string.Empty : string.Empty;
        var gameName = evt.TryGetProperty("category_name", out var gameNameProp) ? gameNameProp.GetString() ?? string.Empty : string.Empty;
        var labels = ParseStringArray(evt, "content_classification_labels");

        try
        {
            await eventBus.PublishAsync(new ChannelUpdated(title, language, gameId, gameName, labels), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChannelUpdateSubscriber: ошибка публикации ChannelUpdated");
        }
    }

    private async Task HandleRevocationAsync(EventSubRevocationArgs args, CancellationToken cancellationToken)
    {
        if (!string.Equals(args.SubscriptionType, SubscriptionType, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogWarning("ChannelUpdateSubscriber: подписка {Type} отозвана ({Status})", args.SubscriptionType, args.Status);
        IsHealthy = false;

        var sessionId = eventSubClient.SessionId;

        if (string.IsNullOrEmpty(sessionId))
        {
            PublishHealthError($"подписка {SubscriptionType} отозвана, нет активной сессии для восстановления");
            return;
        }

        var broadcasterId = await broadcasterIdProvider.GetAsync(cancellationToken);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            PublishHealthError($"подписка {SubscriptionType} отозвана, broadcaster id недоступен");
            return;
        }

        try
        {
            await helix.CreateEventSubSubscriptionAsync(SubscriptionType,
                SubscriptionVersion,
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                },
                sessionId,
                cancellationToken);

            IsHealthy = true;
            logger.LogInformation("ChannelUpdateSubscriber: подписка {Type} восстановлена после revocation", SubscriptionType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChannelUpdateSubscriber: не удалось восстановить подписку {Type} после revocation", SubscriptionType);
            PublishHealthError($"не удалось восстановить подписку {SubscriptionType} после revocation");
        }
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>(arrayProp.GetArrayLength());

        foreach (var item in arrayProp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();

            if (!string.IsNullOrEmpty(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private void PublishHealthError(string message)
    {
        _ = eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Error, "ChannelUpdateSubscriber", $"EventSub channel.update: {message}"));
    }
}
