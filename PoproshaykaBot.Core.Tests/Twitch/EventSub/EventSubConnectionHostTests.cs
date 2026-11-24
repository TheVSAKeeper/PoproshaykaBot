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

    [Test]
    public async Task BroadcasterRoleHost_BotOnlyActiveChannel_DoesNotConnect_AndTogglesOnActiveChannelSwitch()
    {
        SeedAccessToken(PlatformAuthRole.Broadcaster, "broadcaster-token");
        _accountsStore.Mutate(PlatformAuthRole.Broadcaster, a => a.UserId = "107618204");

        var catalogPath = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.json");
        try
        {
            var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, catalogPath);
            catalog.Save(new()
            {
                Channels =
                [
                    new()
                    {
                        Platform = StreamingPlatform.Twitch,
                        ChannelId = "177128531",
                        DisplayName = "bobito217",
                        IsConfigured = true,
                        MonitoringEnabled = true,
                        BotAccountId = "107618204",
                        BotContextId = "default",
                        BotVerifiedSubjectId = "107618204",
                    },
                    new()
                    {
                        Platform = StreamingPlatform.Twitch,
                        ChannelId = "107618204",
                        DisplayName = "thevsakeeper",
                        IsConfigured = true,
                        MonitoringEnabled = true,
                        BroadcasterAccountId = "107618204",
                        BroadcasterContextId = "default",
                        BroadcasterVerifiedSubjectId = "107618204",
                        BotAccountId = "107618204",
                        BotContextId = "default",
                        BotVerifiedSubjectId = "107618204",
                    },
                ],
                Platforms =
                [
                    new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
                ],
            });

            var registry = new PlatformChannelRegistry();
            var target = Substitute.For<PoproshaykaBot.Core.Debugging.ITargetChannelProvider>();
            target.Current.Returns(new PoproshaykaBot.Core.Debugging.TargetChannelState("bobito217", "bobito217", false, true));
            var auth = new PoproshaykaBot.Core.Twitch.TwitchChannelAuthorization(_accountsStore, catalog, registry, target);

            var broadcasterClient = Substitute.For<ITwitchEventSubClient>();
            await using var broadcasterHost = new EventSubConnectionHost(
                PlatformAuthRole.Broadcaster,
                broadcasterClient,
                _accountsStore,
                _eventBus,
                NullLogger<EventSubConnectionHost>.Instance,
                auth);

            await broadcasterHost.StartAsync(NullProgress, CancellationToken.None);
            await broadcasterClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());

            catalog.Mutate(s => s.Platforms[0].ActiveChannelId = "107618204");
            await _eventBus.PublishAsync(new ActiveChannelChanged(
                StreamingPlatform.Twitch,
                new ChannelKey(StreamingPlatform.Twitch, "177128531"),
                new ChannelKey(StreamingPlatform.Twitch, "107618204"),
                "thevsakeeper",
                PoproshaykaBot.Core.Settings.ChannelSwitchMode.Manual));

            await broadcasterClient.Received(1).StartAsync(Arg.Any<CancellationToken>());

            catalog.Mutate(s => s.Platforms[0].ActiveChannelId = "177128531");
            await _eventBus.PublishAsync(new ActiveChannelChanged(
                StreamingPlatform.Twitch,
                new ChannelKey(StreamingPlatform.Twitch, "107618204"),
                new ChannelKey(StreamingPlatform.Twitch, "177128531"),
                "bobito217",
                PoproshaykaBot.Core.Settings.ChannelSwitchMode.Manual));

            await broadcasterClient.Received(1).StopAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    [Test]
    public async Task BotRoleHost_RestartsWebSocket_WhenCatalogReassignsActiveChannelBotContext()
    {
        SeedAccessToken(PlatformAuthRole.Bot, "default-bot-token");
        _accountsStore.Mutate(PlatformAuthRole.Bot, a => a.UserId = "1298214074");
        _accountsStore.Mutate(PlatformAuthRole.Bot, "tw-bot-107618204", a =>
        {
            a.AccessToken = "alt-bot-token";
            a.UserId = "107618204";
            a.Login = "thevsakeeper";
        });

        var catalogPath = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.json");
        try
        {
            var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, catalogPath);
            catalog.Save(new()
            {
                Channels =
                [
                    new()
                    {
                        Platform = StreamingPlatform.Twitch,
                        ChannelId = "107618204",
                        DisplayName = "thevsakeeper",
                        IsConfigured = true,
                        MonitoringEnabled = true,
                        BotAccountId = "1298214074",
                        BotContextId = "default",
                        BotVerifiedSubjectId = "1298214074",
                    },
                ],
                Platforms =
                [
                    new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "107618204" },
                ],
            });

            var registry = new PlatformChannelRegistry();
            var target = Substitute.For<PoproshaykaBot.Core.Debugging.ITargetChannelProvider>();
            target.Current.Returns(new PoproshaykaBot.Core.Debugging.TargetChannelState("thevsakeeper", "thevsakeeper", false, true));
            var auth = new PoproshaykaBot.Core.Twitch.TwitchChannelAuthorization(_accountsStore, catalog, registry, target);

            var botClient = Substitute.For<ITwitchEventSubClient>();
            var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startCount = 0;
            botClient.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (Interlocked.Increment(ref startCount) >= 2)
                {
                    restarted.TrySetResult();
                }

                return Task.CompletedTask;
            });

            await using var botHost = new EventSubConnectionHost(
                PlatformAuthRole.Bot,
                botClient,
                _accountsStore,
                _eventBus,
                NullLogger<EventSubConnectionHost>.Instance,
                auth,
                catalog);

            await botHost.StartAsync(NullProgress, CancellationToken.None);
            Assert.That(startCount, Is.EqualTo(1));

            catalog.Mutate(s =>
            {
                s.Channels[0].BotAccountId = "107618204";
                s.Channels[0].BotContextId = "tw-bot-107618204";
                s.Channels[0].BotVerifiedSubjectId = "107618204";
            });

            await restarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await botClient.Received(1).StopAsync(Arg.Any<CancellationToken>());
            await botClient.Received(2).StartAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    [Test]
    public async Task Disconnect_WithCloseCode4003_StopsAfterMaxAttempts_DespiteSessionWelcome()
    {
        await _host.StartAsync(NullProgress, CancellationToken.None);
        _statusEvents.Clear();

        for (var i = 0; i < 6; i++)
        {
            _eventSubClient.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
                new EventSubSessionWelcomeArgs($"session-{i}", 10),
                CancellationToken.None);

            _eventSubClient.OnDisconnected += Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(
                new EventSubDisconnectedArgs("Сессия EventSub завершена", 4003),
                CancellationToken.None);
        }

        Assert.That(_statusEvents.Select(e => e.Status), Does.Contain(StreamMonitoringStatus.Failed));
    }
}
