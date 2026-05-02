using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Twitch.EventSub;

namespace PoproshaykaBot.WinForms.Tests.Twitch.EventSub;

[TestFixture]
public sealed class EventSubConnectionHostTests
{
    [SetUp]
    public void SetUp()
    {
        _eventSubClient = Substitute.For<ITwitchEventSubClient>();
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _statusEvents = [];
        _statusSubscription = _eventBus.Subscribe<StreamMonitoringStatusChanged>(e => _statusEvents.Add(e));

        _host = new(_eventSubClient, _eventBus, NullLogger<EventSubConnectionHost>.Instance);
    }

    [TearDown]
    public async ValueTask TearDown()
    {
        _statusSubscription.Dispose();
        await _host.DisposeAsync();
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });

    private ITwitchEventSubClient _eventSubClient = null!;
    private InMemoryEventBus _eventBus = null!;
    private List<StreamMonitoringStatusChanged> _statusEvents = null!;
    private IDisposable _statusSubscription = null!;
    private EventSubConnectionHost _host = null!;

    [Test]
    public async Task TwitchAuthorizationRefreshed_BotRole_RestartsRunningWebSocket()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));

        await _eventSubClient.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_BroadcasterRole_DoesNotRestart()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Broadcaster));

        await _eventSubClient.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_WhenNotRunning_StartsWebSocket()
    {
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));

        await _eventSubClient.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_PublishesConnectingStatus()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Connecting));
    }

    [Test]
    public async Task SessionWelcome_PublishesConnectedStatus()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _statusEvents.Clear();

        _eventSubClient.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(new EventSubSessionWelcomeArgs("session-1", null),
            CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Connected));
    }

    [Test]
    public async Task StopAsync_PublishesDisconnectedStatus()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _statusEvents.Clear();

        await _host.StopAsync(NullProgress, CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Disconnected));
    }

    [Test]
    public async Task Disconnect_WithRetriesAvailable_PublishesReconnectingStatus()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _statusEvents.Clear();

        _eventSubClient.OnDisconnected += Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(new EventSubDisconnectedArgs("connection-lost"),
            CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Reconnecting));
    }
}
