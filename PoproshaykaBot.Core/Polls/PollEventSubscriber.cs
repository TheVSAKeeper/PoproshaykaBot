using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Polling;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.Core.Polls;

public sealed class PollEventSubscriber(
    ITwitchEventSubClient eventSubClient,
    [FromKeyedServices(TwitchEndpoints.HelixBroadcasterClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    PollsAvailabilityService availability,
    IEventBus eventBus,
    ILogger<PollEventSubscriber> logger)
    : IHostedComponent
{
    private static readonly string[] SubscriptionTypes =
    [
        "channel.poll.begin",
        "channel.poll.progress",
        "channel.poll.end",
    ];

    private bool _subscribed;

    public bool IsHealthy { get; private set; } = true;

    public string Name => "Подписка на EventSub голосований";

    public int StartOrder => 260;

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

        logger.LogInformation("PollEventSubscriber: хуки EventSub установлены");

        if (eventSubClient.SessionId is { } sessionId)
        {
            return HandleSessionWelcomeAsync(new(sessionId, null), cancellationToken);
        }

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

    private async Task HandleSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken ct)
    {
        var availabilityResult = await availability.GetAsync(ct);

        if (!availabilityResult.IsAvailable)
        {
            logger.LogInformation("PollEventSubscriber: подписки не создаются — {Reason}", availabilityResult.UnavailableReason);
            IsHealthy = false;
            return;
        }

        var broadcasterId = await broadcasterIdProvider.GetAsync(ct);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            logger.LogError("PollEventSubscriber: не удалось получить broadcaster id");
            IsHealthy = false;
            return;
        }

        IsHealthy = true;

        foreach (var type in SubscriptionTypes)
        {
            try
            {
                await helix.CreateEventSubSubscriptionAsync(type,
                    "1",
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = broadcasterId,
                    },
                    args.SessionId,
                    ct);

                logger.LogInformation("PollEventSubscriber: подписка на {Type} создана", type);
            }
            catch (HelixRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogInformation("PollEventSubscriber: подписка {Type} уже существует для текущей EventSub-сессии — переиспользуем", type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PollEventSubscriber: ошибка подписки на {Type}", type);
                IsHealthy = false;
            }
        }

        await TryRecoverActivePollAsync(broadcasterId, ct);
    }

    private async Task HandleNotificationAsync(EventSubNotificationArgs args, CancellationToken ct)
    {
        if (!args.SubscriptionType.StartsWith("channel.poll.", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await JsonEventProjector.ProjectAsync(args, eventBus, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PollEventSubscriber: ошибка обработки {Type}", args.SubscriptionType);
        }
    }

    private Task HandleRevocationAsync(EventSubRevocationArgs args, CancellationToken ct)
    {
        if (args.SubscriptionType.StartsWith("channel.poll.", StringComparison.Ordinal))
        {
            logger.LogWarning("PollEventSubscriber: подписка {Type} отозвана ({Status})", args.SubscriptionType, args.Status);
            IsHealthy = false;
        }

        return Task.CompletedTask;
    }

    private async Task TryRecoverActivePollAsync(string broadcasterId, CancellationToken ct)
    {
        try
        {
            var polls = await helix.GetPollsAsync(broadcasterId, "ACTIVE", 1, ct);
            var active = polls.FirstOrDefault();

            if (active is null)
            {
                return;
            }

            var snapshot = PollEventSubMapper.FromHelix(active, null);
            logger.LogInformation("PollEventSubscriber: найдено активное голосование {PollId}, восстанавливаем", active.Id);
            await eventBus.PublishAsync(new PollStarted(snapshot), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PollEventSubscriber: не удалось проверить активное голосование");
        }
    }

    private static class JsonEventProjector
    {
        public static async Task ProjectAsync(EventSubNotificationArgs args, IEventBus bus, CancellationToken ct)
        {
            switch (args.SubscriptionType)
            {
                case "channel.poll.begin":
                    await bus.PublishAsync(new PollStarted(PollEventSubMapper.FromEventSubBegin(args.Payload)), ct);
                    break;

                case "channel.poll.progress":
                    await bus.PublishAsync(new PollProgressed(PollEventSubMapper.FromEventSubProgress(args.Payload)), ct);
                    break;

                case "channel.poll.end":
                    var snapshot = PollEventSubMapper.FromEventSubEnd(args.Payload);

                    switch (snapshot.Status)
                    {
                        case PollSnapshotStatus.Completed:
                            var (winner, isTie) = PollEventSubMapper.DetectWinner(snapshot);
                            await bus.PublishAsync(new PollFinalized(snapshot, winner, isTie), ct);
                            break;

                        case PollSnapshotStatus.Terminated:
                            await bus.PublishAsync(new PollTerminated(snapshot), ct);
                            break;

                        case PollSnapshotStatus.Archived:
                            await bus.PublishAsync(new PollArchived(snapshot), ct);
                            break;

                        default:
                            await bus.PublishAsync(new PollFinalized(snapshot, null, false), ct);
                            break;
                    }

                    break;
            }
        }
    }
}
