using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Polling;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Polls;

[TestFixture]
public sealed class PollEventSubscriberTests
{
    [SetUp]
    public void SetUp()
    {
        _eventSubClient = Substitute.For<ITwitchEventSubClient>();

        _helix = Substitute.For<ITwitchHelixClient>();
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("sub-id");

        _helix.GetPollsAsync(Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HelixPollInfo>());

        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(BroadcasterId);

        _availability = Substitute.For<PollsAvailabilityService>(Substitute.For<ITwitchHelixClient>(),
            _broadcasterIdProvider,
            new FakeTargetChannelProvider(),
            new AccountsStore(),
            NullLogger<PollsAvailabilityService>.Instance);

        _availability.GetAsync(Arg.Any<CancellationToken>()).Returns(PollsAvailability.Available);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);

        _subscriber = new(_eventSubClient,
            _helix,
            _broadcasterIdProvider,
            _availability,
            _eventBus,
            NullLogger<PollEventSubscriber>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subscriber.StopAsync(NullProgress, CancellationToken.None);
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });
    private const string BroadcasterId = "12345";

    private ITwitchEventSubClient _eventSubClient = null!;
    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private PollsAvailabilityService _availability = null!;
    private InMemoryEventBus _eventBus = null!;
    private PollEventSubscriber _subscriber = null!;

    [Test]
    public async Task StartAsync_WhenSessionAlreadyActive_CreatesPollSubscriptionsImmediately()
    {
        _eventSubClient.SessionId.Returns("existing-session");

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        foreach (var type in new[] { "channel.poll.begin", "channel.poll.progress", "channel.poll.end" })
        {
            await _helix.Received(1)
                .CreateEventSubSubscriptionAsync(type,
                    "1",
                    Arg.Is<IReadOnlyDictionary<string, string>>(d => d["broadcaster_user_id"] == BroadcasterId),
                    "existing-session",
                    Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task Остановка_удаляет_созданные_и_переиспользованные_по_409_подписки()
    {
        _helix.CreateEventSubSubscriptionAsync("channel.poll.progress",
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HelixRequestException(HttpMethod.Post,
                "helix/eventsub/subscriptions",
                System.Net.HttpStatusCode.Conflict,
                "subscription already exists",
                null));

        _helix.GetEventSubSubscriptionsByUserAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns([
                new EventSubSubscriptionInfo("sub-progress",
                    "channel.poll.progress",
                    "enabled",
                    "session-1",
                    new Dictionary<string, string> { ["broadcaster_user_id"] = BroadcasterId }),
            ]);

        _eventSubClient.SessionId.Returns("session-1");
        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_subscriber.IsHealthy, Is.True);

        await _subscriber.StopAsync(NullProgress, CancellationToken.None);

        await _helix.Received(1).DeleteEventSubSubscriptionAsync("sub-progress", Arg.Any<CancellationToken>());
        await _helix.Received(3).DeleteEventSubSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void PollEventSubscriber_ImplementsStreamHostedComponent_AfterFix()
    {
        Assert.That(typeof(PollEventSubscriber).GetInterfaces(),
            Does.Contain(typeof(IStreamHostedComponent)),
            "голосования принадлежат broadcaster-сессии – подписчик должен быть IStreamHostedComponent наравне с ChannelUpdateSubscriber, иначе IRC-дисконнект бота гасит channel.poll.*");
    }

    [Test]
    public async Task StartAsync_WhenNoActiveSession_DoesNotCallHelixUntilWelcome()
    {
        _eventSubClient.SessionId.Returns((string?)null);

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        await _helix.DidNotReceive()
            .CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [TestCase("COMPLETED")]
    [TestCase("ARCHIVED")]
    [TestCase("TERMINATED")]
    [TestCase("SOMETHING_NEW")]
    public async Task StartAsync_WhenHelixReturnsNoActivePolls_DoesNotPublishPollStarted(string status)
    {
        _eventSubClient.SessionId.Returns("existing-session");
        _helix.GetPollsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Poll("finished", status, DateTime.UtcNow.AddDays(-30))]);

        var started = new List<PollStarted>();
        using var subscription = _eventBus.Subscribe<PollStarted>(started.Add);

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(started, Is.Empty,
            "Get Polls отдаёт и завершённые, и незнакомые по статусу голосования – восстанавливать из них нечего");
    }

    [Test]
    public async Task StartAsync_WhenHelixReturnsFinishedAndActivePolls_PublishesActiveOnce()
    {
        _eventSubClient.SessionId.Returns("existing-session");
        _helix.GetPollsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Poll("finished", "COMPLETED", DateTime.UtcNow.AddDays(-30)),
                Poll("running", "ACTIVE", null),
            ]);

        var started = new List<PollStarted>();
        using var subscription = _eventBus.Subscribe<PollStarted>(started.Add);

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Has.Count.EqualTo(1));
            Assert.That(started[0].Snapshot.PollId, Is.EqualTo("running"));
        }
    }

    private static HelixPollInfo Poll(string id, string status, DateTime? endedAt)
    {
        return new(id,
            BroadcasterId,
            "Вопрос?",
            [new("c1", "А", 3, 0, 0), new("c2", "Б", 1, 0, 0)],
            false,
            0,
            status,
            60,
            DateTime.UtcNow.AddMinutes(-5),
            endedAt);
    }
}
