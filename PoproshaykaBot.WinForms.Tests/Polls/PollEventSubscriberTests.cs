using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Tests.Polls;

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
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HelixPollInfo>());

        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(BroadcasterId);

        _availability = Substitute.For<PollsAvailabilityService>(Substitute.For<ITwitchHelixClient>(),
            _broadcasterIdProvider,
            CreateSettingsManager(),
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

    private static SettingsManager CreateSettingsManager()
    {
        var manager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        manager.Current.Returns(new AppSettings());
        return manager;
    }
}
