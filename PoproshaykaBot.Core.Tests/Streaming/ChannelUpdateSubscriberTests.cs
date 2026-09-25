using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class ChannelUpdateSubscriberTests
{
    [SetUp]
    public void SetUp()
    {
        _eventSubClient = Substitute.For<ITwitchEventSubClient>();
        _eventSubClient.SessionId.Returns("session-1");

        _helix = Substitute.For<ITwitchHelixClient>();
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("new-sub-id");

        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(BroadcasterId);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);

        _subscriber = new(_eventSubClient, _helix, _broadcasterIdProvider, _eventBus,
            NullLogger<ChannelUpdateSubscriber>.Instance);
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
    private InMemoryEventBus _eventBus = null!;
    private ChannelUpdateSubscriber _subscriber = null!;

    [Test]
    public async Task HandleRevocation_OnChannelUpdate_RecreatesSubscription()
    {
        await _subscriber.StartAsync(NullProgress, CancellationToken.None);
        _helix.ClearReceivedCalls();

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(new EventSubRevocationArgs("old-sub", "channel.update", "user_removed"),
            CancellationToken.None);

        await _helix.Received(1)
            .CreateEventSubSubscriptionAsync("channel.update",
                "2",
                Arg.Is<IReadOnlyDictionary<string, string>>(d => d["broadcaster_user_id"] == BroadcasterId),
                "session-1",
                Arg.Any<CancellationToken>());

        Assert.That(_subscriber.IsHealthy, Is.True);
    }

    [Test]
    public async Task HandleRevocation_OnUnrelatedSubscription_DoesNotCallHelix()
    {
        await _subscriber.StartAsync(NullProgress, CancellationToken.None);
        _helix.ClearReceivedCalls();

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(new EventSubRevocationArgs("old-sub", "stream.online", "version_removed"),
            CancellationToken.None);

        await _helix.DidNotReceive()
            .CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleRevocation_WithoutSession_DoesNotCallHelix()
    {
        _eventSubClient.SessionId.Returns((string?)null);
        await _subscriber.StartAsync(NullProgress, CancellationToken.None);
        _helix.ClearReceivedCalls();

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(new EventSubRevocationArgs("old-sub", "channel.update", "user_removed"),
            CancellationToken.None);

        await _helix.DidNotReceive()
            .CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

        Assert.That(_subscriber.IsHealthy, Is.False);
    }

    [TestCase("session-1", "enabled", true, TestName = "409 с подпиской своей сессии – переиспользуется и удаляется при остановке")]
    [TestCase("session-other", "enabled", false, TestName = "409 с подпиской чужой активной сессии – нездоров и чужую не трогает")]
    public async Task Ответ_409_решается_по_сессии_найденной_подписки(string sessionId, string status, bool reused)
    {
        _helix.CreateEventSubSubscriptionAsync("channel.update",
                "2",
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HelixRequestException(HttpMethod.Post,
                "helix/eventsub/subscriptions",
                HttpStatusCode.Conflict,
                "subscription already exists",
                null));

        _helix.GetEventSubSubscriptionsByUserAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns([
                new EventSubSubscriptionInfo("sub-existing",
                    "channel.update",
                    status,
                    sessionId,
                    new Dictionary<string, string> { ["broadcaster_user_id"] = BroadcasterId }),
            ]);

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnSessionWelcome +=
            Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(new EventSubSessionWelcomeArgs("session-1", 60),
                CancellationToken.None);

        Assert.That(_subscriber.IsHealthy, Is.EqualTo(reused));

        await _subscriber.StopAsync(NullProgress, CancellationToken.None);

        await _helix.Received(reused ? 1 : 0).DeleteEventSubSubscriptionAsync("sub-existing", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Остановка_удаляет_подписку_восстановленную_после_отзыва()
    {
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("sub-first", "sub-restored");

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnSessionWelcome +=
            Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(new EventSubSessionWelcomeArgs("session-1", 60),
                CancellationToken.None);

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(new EventSubRevocationArgs("sub-first", "channel.update", "user_removed"),
            CancellationToken.None);

        await _subscriber.StopAsync(NullProgress, CancellationToken.None);

        await _helix.Received(1).DeleteEventSubSubscriptionAsync("sub-restored", Arg.Any<CancellationToken>());
        await _helix.DidNotReceive().DeleteEventSubSubscriptionAsync("sub-first", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleRevocation_WhenResubscribeFails_KeepsUnhealthy()
    {
        _helix.CreateEventSubSubscriptionAsync("channel.update",
                "2",
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                "session-1",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _subscriber.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(new EventSubRevocationArgs("old-sub", "channel.update", "user_removed"),
            CancellationToken.None);

        Assert.That(_subscriber.IsHealthy, Is.False);
    }
}
