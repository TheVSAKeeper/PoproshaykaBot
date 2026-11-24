using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class StreamStatusManagerMultiContextTests
{
    private DirectoryInfo _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("ssm-multi-ctx");
    }

    [TearDown]
    public void TearDown()
    {
        _dir.Delete(recursive: true);
    }

    [Test]
    public async Task CreateEventSubSubscriptions_UsesSingleSessionBotContext_AcrossMultipleMonitoredChannels()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    Handle = "bobito217",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "107618204",
                    BotContextId = "tw-bot-107618204",
                    BotVerifiedSubjectId = "107618204",
                },
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "90020006",
                    DisplayName = "tsm_viss",
                    Handle = "tsm_viss",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "1298214074",
                    BotContextId = "default",
                    BotVerifiedSubjectId = "1298214074",
                },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
            ],
        });

        var capturedContexts = new List<(string BroadcasterId, string? ContextId)>();
        var helix = Substitute.For<ITwitchHelixClient>();
        helix.CreateEventSubSubscriptionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var condition = call.ArgAt<IReadOnlyDictionary<string, string>>(2);
                capturedContexts.Add((condition["broadcaster_user_id"], PlatformAuthRequestOptions.CurrentContextId));
                return Task.FromResult($"sub-{capturedContexts.Count}");
            });
        helix.GetStreamsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HelixStreamInfo>>([]));

        var eventSub = Substitute.For<ITwitchEventSubClient>();
        eventSub.SessionId.Returns("session-1");

        var broadcasterId = Substitute.For<IBroadcasterIdProvider>();
        broadcasterId.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("177128531"));

        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var registry = new PlatformChannelRegistry();
        var auth = Substitute.For<IPlatformAuthService>();
        auth.HasUsableAccessToken(PlatformAuthRole.Bot).Returns(true);
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "tw-bot-107618204").Returns(true);
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "default").Returns(true);

        await using var manager = new StreamStatusManager(
            eventSub,
            helix,
            broadcasterId,
            target,
            settings,
            bus,
            TimeProvider.System,
            NullLogger<StreamStatusManager>.Instance,
            catalog,
            registry,
            auth);

        await manager.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);

        eventSub.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-1", 10),
            CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (capturedContexts.Count < 4 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Multiple(() =>
        {
            Assert.That(capturedContexts, Has.Count.EqualTo(4));
            Assert.That(capturedContexts.Select(static c => c.BroadcasterId).Distinct(), Is.EquivalentTo(new[] { "177128531", "90020006" }));
            Assert.That(capturedContexts.Select(static c => c.ContextId).Distinct(), Is.EqualTo(new[] { "tw-bot-107618204" }));
        });
    }

    [Test]
    public void ChannelCatalogStore_EnsureChannelKnown_CaseOnlyDifference_DoesNotEmitChanged()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    KnownAliases = ["BOBito217"],
                    IsConfigured = true,
                    MonitoringEnabled = true,
                },
            ],
        });

        var changedCount = 0;
        catalog.Changed += (_, _) => changedCount++;

        catalog.EnsureChannelKnown(new ChannelKey(StreamingPlatform.Twitch, "177128531"), "BOBito217");

        Assert.That(changedCount, Is.Zero);
    }

    [Test]
    public async Task CreateEventSubSubscriptions_PublishesChannelMonitoringStateChanged_WhenSlotRecoversToActive()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    Handle = "bobito217",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "1298214074",
                    BotContextId = "default",
                    BotVerifiedSubjectId = "1298214074",
                },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
            ],
        });

        var subCount = 0;
        var helix = Substitute.For<ITwitchHelixClient>();
        helix.CreateEventSubSubscriptionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult($"sub-{Interlocked.Increment(ref subCount)}"));
        helix.GetStreamsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HelixStreamInfo>>([]));

        var eventSub = Substitute.For<ITwitchEventSubClient>();
        eventSub.SessionId.Returns("session-1");

        var broadcasterId = Substitute.For<IBroadcasterIdProvider>();
        broadcasterId.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("177128531"));

        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var stateEvents = new List<ChannelMonitoringStateChanged>();
        using var sub = bus.Subscribe<ChannelMonitoringStateChanged>(evt =>
        {
            lock (stateEvents)
            {
                stateEvents.Add(evt);
            }
        });

        var registry = new PlatformChannelRegistry();
        var auth = Substitute.For<IPlatformAuthService>();
        auth.HasUsableAccessToken(PlatformAuthRole.Bot).Returns(false);

        await using var manager = new StreamStatusManager(
            eventSub,
            helix,
            broadcasterId,
            target,
            settings,
            bus,
            TimeProvider.System,
            NullLogger<StreamStatusManager>.Instance,
            catalog,
            registry,
            auth);

        await manager.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);

        eventSub.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-1", 10),
            CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lock (stateEvents)
            {
                if (stateEvents.Any(e => e.Snapshot.Availability == ChannelMonitoringAvailability.Unavailable))
                {
                    break;
                }
            }

            await Task.Delay(20);
        }

        auth.HasUsableAccessToken(PlatformAuthRole.Bot).Returns(true);
        eventSub.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-2", 10),
            CancellationToken.None);

        deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lock (stateEvents)
            {
                if (stateEvents.Any(e => e.Snapshot.Availability == ChannelMonitoringAvailability.Active))
                {
                    break;
                }
            }

            await Task.Delay(20);
        }

        lock (stateEvents)
        {
            Assert.That(stateEvents.Select(static e => e.Snapshot.Availability),
                Does.Contain(ChannelMonitoringAvailability.Unavailable));
            Assert.That(stateEvents[^1].Snapshot.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(stateEvents[^1].Snapshot.Reason, Is.EqualTo(MonitoringUnavailableReason.None));
        }
    }

    [Test]
    public async Task RefreshAsync_SkipsChannelWithMissingBotAuth_AndStillFetchesValidChannel()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    Handle = "bobito217",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "107618204",
                    BotContextId = "tw-bot-107618204",
                    BotVerifiedSubjectId = "107618204",
                },
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "107618204",
                    DisplayName = "thevsakeeper",
                    Handle = "thevsakeeper",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "1298214074",
                    BotContextId = "default",
                    BotVerifiedSubjectId = "1298214074",
                },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
            ],
        });

        var helix = Substitute.For<ITwitchHelixClient>();
        helix.GetStreamAsync("177128531", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HelixStreamInfo?>(new(
                "stream-1", "177128531", "bobito217", "bobito217", "1", "Escape from Tarkov", "live", "Title", 3500,
                DateTime.UtcNow.AddHours(-1), "ru", "", [], false)));

        var eventSub = Substitute.For<ITwitchEventSubClient>();
        var broadcasterId = Substitute.For<IBroadcasterIdProvider>();
        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var registry = new PlatformChannelRegistry();
        var auth = Substitute.For<IPlatformAuthService>();
        auth.HasUsableAccessToken(PlatformAuthRole.Bot).Returns(false);
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "tw-bot-107618204").Returns(true);

        await using var manager = new StreamStatusManager(
            eventSub,
            helix,
            broadcasterId,
            target,
            settings,
            bus,
            TimeProvider.System,
            NullLogger<StreamStatusManager>.Instance,
            catalog,
            registry,
            auth);

        await manager.RefreshAsync(null, CancellationToken.None);

        var validSnapshot = manager.GetSnapshot(new(StreamingPlatform.Twitch, "177128531"));
        var missingAuthSnapshot = manager.GetSnapshot(new(StreamingPlatform.Twitch, "107618204"));

        Assert.Multiple(() =>
        {
            Assert.That(validSnapshot.Status, Is.EqualTo(StreamStatus.Online));
            Assert.That(validSnapshot.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(missingAuthSnapshot.Availability, Is.EqualTo(ChannelMonitoringAvailability.Unavailable));
            Assert.That(missingAuthSnapshot.Reason, Is.EqualTo(MonitoringUnavailableReason.MissingBotAuth));
        });
        await helix.DidNotReceive().GetStreamAsync("107618204", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwitchChatPlatform_IsConnected_ReflectsPlatformHostRunningState()
    {
        var channels = new PlatformChannelRegistry();
        channels.SetBinding(new(new(StreamingPlatform.Twitch, "177128531"), "bobito217", "tw-bot-107618204", "default", 1));
        var accountsStore = new AccountsStore(NullLogger<AccountsStore>.Instance, Path.Combine(_dir.FullName, "accounts.json"));
        var component = Substitute.For<PoproshaykaBot.Core.Infrastructure.Hosting.IHostedComponent>();
        component.Name.Returns("chat");
        var host = new PoproshaykaBot.Core.Infrastructure.Hosting.PlatformHost(
            StreamingPlatform.Twitch,
            [component],
            NullLogger<PoproshaykaBot.Core.Infrastructure.Hosting.PlatformHost>.Instance);
        var chatSender = new PoproshaykaBot.Core.Twitch.Chat.ChatSender(
            Substitute.For<ITwitchHelixClient>(),
            Substitute.For<IBroadcasterIdProvider>(),
            Substitute.For<PoproshaykaBot.Core.Twitch.Chat.IBotUserIdProvider>(),
            Substitute.For<ITargetChannelProvider>(),
            new(TimeProvider.System),
            NullLogger<PoproshaykaBot.Core.Twitch.Chat.ChatSender>.Instance);
        var messenger = new PoproshaykaBot.Core.Chat.TwitchChatMessenger(
            chatSender,
            accountsStore,
            NullLogger<PoproshaykaBot.Core.Chat.TwitchChatMessenger>.Instance);
        var decorations = new PoproshaykaBot.Core.Chat.ChatDecorationsProvider(Substitute.For<ITwitchHelixClient>());

        var platform = new TwitchChatPlatform(
            Substitute.For<IStreamStatus>(),
            messenger,
            decorations,
            channels,
            accountsStore,
            platformHost: host);

        Assert.That(platform.IsConnected, Is.False);

        await host.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        Assert.That(platform.IsConnected, Is.True);

        await host.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);
        Assert.That(platform.IsConnected, Is.False);
    }

    [Test]
    public async Task CreateEventSubSubscriptions_SerializesConcurrentSessionWelcomeCalls()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    Handle = "bobito217",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "107618204",
                    BotContextId = "tw-bot-107618204",
                    BotVerifiedSubjectId = "107618204",
                },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
            ],
        });

        var inFlight = 0;
        var maxInFlight = 0;
        var totalCalls = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var helix = Substitute.For<ITwitchHelixClient>();
        helix.CreateEventSubSubscriptionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var current = Interlocked.Increment(ref inFlight);
                var snapshotMax = Volatile.Read(ref maxInFlight);
                while (current > snapshotMax)
                {
                    var prev = Interlocked.CompareExchange(ref maxInFlight, current, snapshotMax);
                    if (prev == snapshotMax)
                    {
                        break;
                    }

                    snapshotMax = prev;
                }

                var callNumber = Interlocked.Increment(ref totalCalls);
                if (callNumber == 1)
                {
                    firstEntered.TrySetResult();
                    await releaseFirst.Task;
                }

                Interlocked.Decrement(ref inFlight);
                return $"sub-{callNumber}";
            });
        helix.GetStreamsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HelixStreamInfo>>([]));

        EventSubAsyncHandler<EventSubSessionWelcomeArgs>? welcomeHandler = null;
        var eventSub = Substitute.For<ITwitchEventSubClient>();
        eventSub.SessionId.Returns("session-1");
        eventSub.When(x => x.OnSessionWelcome += Arg.Any<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>())
            .Do(call => welcomeHandler += call.Arg<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>());

        var broadcasterId = Substitute.For<IBroadcasterIdProvider>();
        broadcasterId.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("177128531"));

        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var registry = new PlatformChannelRegistry();
        var auth = Substitute.For<IPlatformAuthService>();
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "tw-bot-107618204").Returns(true);

        await using var manager = new StreamStatusManager(
            eventSub,
            helix,
            broadcasterId,
            target,
            settings,
            bus,
            TimeProvider.System,
            NullLogger<StreamStatusManager>.Instance,
            catalog,
            registry,
            auth);

        await manager.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        Assert.That(welcomeHandler, Is.Not.Null);

        var firstTask = welcomeHandler!(new EventSubSessionWelcomeArgs("session-1", 10), CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var secondTask = welcomeHandler!(new EventSubSessionWelcomeArgs("session-1", 10), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(secondTask.IsCompleted, Is.False);
            Assert.That(Volatile.Read(ref totalCalls), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref maxInFlight), Is.EqualTo(1));
        });

        releaseFirst.TrySetResult();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref totalCalls), Is.EqualTo(4));
            Assert.That(Volatile.Read(ref maxInFlight), Is.EqualTo(1));
        });
    }
}
