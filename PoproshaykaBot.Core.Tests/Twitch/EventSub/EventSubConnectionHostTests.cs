using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;

namespace PoproshaykaBot.Core.Tests.Twitch.EventSub;

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

        _accountsFilePath = Path.Combine(Path.GetTempPath(), $"accounts-{Guid.NewGuid():N}.json");
        _accountsStore = new(NullLogger<AccountsStore>.Instance, _accountsFilePath);
        SeedAccessToken(PlatformAuthRole.Bot, "bot-token");

        _host = new(PlatformAuthRole.Bot, _eventSubClient, _accountsStore, _eventBus, NullLogger<EventSubConnectionHost>.Instance);
    }

    [TearDown]
    public async ValueTask TearDown()
    {
        _statusSubscription.Dispose();
        await _host.DisposeAsync();

        if (File.Exists(_accountsFilePath))
        {
            File.Delete(_accountsFilePath);
        }
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });

    private ITwitchEventSubClient _eventSubClient = null!;
    private InMemoryEventBus _eventBus = null!;
    private List<StreamMonitoringStatusChanged> _statusEvents = null!;
    private IDisposable _statusSubscription = null!;
    private AccountsStore _accountsStore = null!;
    private string _accountsFilePath = null!;
    private EventSubConnectionHost _host = null!;

    private void SeedAccessToken(PlatformAuthRole role, string token)
    {
        _accountsStore.Mutate(role, account =>
        {
            account.AccessToken = token;
            account.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            if (account.Scopes.Length == 0)
            {
                account.Scopes = [..role == PlatformAuthRole.Bot ? TwitchScopes.BotRequired : TwitchScopes.BroadcasterRequired];
            }
        });
    }

    private void ClearAccessToken(PlatformAuthRole role)
    {
        _accountsStore.Mutate(role, account =>
        {
            account.AccessToken = string.Empty;
            account.AccessTokenExpiresAt = null;
        });
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_BotRole_RestartsRunningWebSocket()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot));

        await _eventSubClient.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_BroadcasterRole_DoesNotRestart()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster));

        await _eventSubClient.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_WhenNotRunning_StartsWebSocket()
    {
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot));

        await _eventSubClient.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await _eventSubClient.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchAuthorizationRefreshed_WhenNoAccessToken_DoesNotStartWebSocket()
    {
        ClearAccessToken(PlatformAuthRole.Bot);
        _eventSubClient.ClearReceivedCalls();

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot));

        await _eventSubClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
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

    [Test]
    public async Task StartAsync_WithoutAccessToken_DoesNotConnect()
    {
        ClearAccessToken(PlatformAuthRole.Bot);
        _eventSubClient.ClearReceivedCalls();
        _statusEvents.Clear();

        await _host.StartAsync(NullProgress, CancellationToken.None);

        await _eventSubClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        Assert.That(_statusEvents, Is.Empty);
    }

    [Test]
    public async Task BroadcasterRoleHost_StartAsync_PublishesConnectingStatus_AfterFix()
    {
        SeedAccessToken(PlatformAuthRole.Broadcaster, "broadcaster-token");

        var broadcasterClient = Substitute.For<ITwitchEventSubClient>();
        await using var broadcasterHost = new EventSubConnectionHost(PlatformAuthRole.Broadcaster,
            broadcasterClient,
            _accountsStore,
            _eventBus,
            NullLogger<EventSubConnectionHost>.Instance);

        _statusEvents.Clear();

        await broadcasterHost.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Connecting),
            "broadcaster-сессия – равноправный источник статуса; молчаливый отказ broadcaster-WS оставляет UI без сигнала и тихо ломает channel.update/channel.poll.*");
    }

    [Test]
    public async Task BroadcasterRoleHost_StartAsync_PublishesStatusWithBroadcasterRole()
    {
        SeedAccessToken(PlatformAuthRole.Broadcaster, "broadcaster-token");

        var broadcasterClient = Substitute.For<ITwitchEventSubClient>();
        await using var broadcasterHost = new EventSubConnectionHost(PlatformAuthRole.Broadcaster,
            broadcasterClient,
            _accountsStore,
            _eventBus,
            NullLogger<EventSubConnectionHost>.Instance);

        _statusEvents.Clear();

        await broadcasterHost.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_statusEvents, Is.Not.Empty);
        Assert.That(_statusEvents, Has.All.With.Property(nameof(StreamMonitoringStatusChanged.Role)).EqualTo(PlatformAuthRole.Broadcaster));
    }

    [Test]
    public async Task BotRoleHost_PublishesStatusWithBotRole()
    {
        _statusEvents.Clear();

        await _host.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_statusEvents, Has.All.With.Property(nameof(StreamMonitoringStatusChanged.Role)).EqualTo(PlatformAuthRole.Bot));
    }

    [Test]
    public async Task BroadcasterRoleHost_Disconnect_PublishesReconnectingStatus_AfterFix()
    {
        SeedAccessToken(PlatformAuthRole.Broadcaster, "broadcaster-token");

        var broadcasterClient = Substitute.For<ITwitchEventSubClient>();
        await using var broadcasterHost = new EventSubConnectionHost(PlatformAuthRole.Broadcaster,
            broadcasterClient,
            _accountsStore,
            _eventBus,
            NullLogger<EventSubConnectionHost>.Instance);

        await broadcasterHost.StartAsync(NullProgress, CancellationToken.None);
        _statusEvents.Clear();

        broadcasterClient.OnDisconnected += Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(new EventSubDisconnectedArgs("connection-lost"),
            CancellationToken.None);

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Reconnecting),
            "обрыв broadcaster-WebSocket'а должен сигналиться в UI наравне с бот-сессией");
    }
}
