using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Infrastructure.Events.Polling;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Polls.Handlers;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class BroadcastMessengerRoutingTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-broadcast-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public async Task PollAnnouncements_GoOnlyToTwitch_EvenWhenYouTubeConnected()
    {
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", isConnected: true);
        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", isConnected: true);
        var messenger = new BroadcastMessenger(new ChatPlatformRegistry([twitch, youtube]), NullLogger<BroadcastMessenger>.Instance);

        var polls = new PollsSettings();
        polls.ChatTemplates.StartEnabled = true;
        polls.ChatTemplates.StartTemplate = "Старт: {title}";
        var pollsStore = Substitute.For<PollsStore>(NullLogger<PollsStore>.Instance, null, null);
        pollsStore.Load().Returns(polls);

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var handler = new PollChatAnnouncementHandler(messenger, pollsStore, bus, TimeProvider.System);

        var snapshot = new PollSnapshot("p1", null, "Играем?", [new("1", "Да", 0, 0, 0)], DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), false, 0, PollSnapshotStatus.Active, null);
        await handler.HandleAsync(new PollStarted(snapshot), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(twitch.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "Старт: Играем?" }));
            Assert.That(youtube.FakeMessenger.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task StreamStatusAndFarewell_SendToConnectedPlatforms_AndPartitionFarewellAudience()
    {
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", isConnected: true);
        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", isConnected: true);
        var registry = new ChatPlatformRegistry([twitch, youtube]);
        var messenger = new BroadcastMessenger(registry, NullLogger<BroadcastMessenger>.Instance);

        var settings = new AppSettings
        {
            Twitch =
            {
                Channel = "chan",
                AutoBroadcast =
                {
                    AutoBroadcastEnabled = false,
                    StreamStatusNotificationsEnabled = true,
                    StreamStartMessage = "Поехали!",
                },
                Messages =
                {
                    FarewellEnabled = true,
                    Farewell = "Пока, {username}!",
                    DisconnectionEnabled = false,
                },
            },
        };
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var audience = new AudienceTracker(settingsManager);
        audience.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("t1", "TwitchViewer");
        audience.ForPlatform(StreamingPlatform.YouTube).OnUserMessage("y1", "YouTubeViewer");

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var statusHandler = new StreamStatusBroadcastHandler(
            Substitute.For<IBroadcastScheduler>(),
            messenger,
            twitch.StreamStatus,
            new FakeTargetChannelProvider(),
            settingsManager,
            bus,
            NullLogger<StreamStatusBroadcastHandler>.Instance);
        using var farewellHandler = new FarewellMessageHandler(
            registry,
            audience,
            messenger,
            settingsManager,
            bus,
            NullLogger<FarewellMessageHandler>.Instance);

        await statusHandler.HandleAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected), CancellationToken.None);
        await statusHandler.HandleAsync(new StreamWentOnline(StreamingPlatform.Twitch, "chan", null), CancellationToken.None);
        await farewellHandler.HandleAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnecting), CancellationToken.None);
        await farewellHandler.HandleAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Disconnecting), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(twitch.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "Поехали!", "Пока, TwitchViewer!" }));
            Assert.That(youtube.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "Поехали!", "Пока, YouTubeViewer!" }));
        });

        youtube.IsConnected = false;
        messenger.SendToConnected("только подключённым");
        Assert.That(twitch.FakeMessenger.Sent[^1].Text, Is.EqualTo("только подключённым"));
        Assert.That(youtube.FakeMessenger.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task BroadcastScheduler_UsesTargetsProvider_AndRendersSnapshotPerPlatform()
    {
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", isConnected: true);
        twitch.FakeStreamStatus.SetOnline(new() { Title = "Twitch Title", GameName = "Chess", ViewerCount = 100 }, DateTimeOffset.UtcNow);

        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", isConnected: true);
        youtube.FakeStreamStatus.SetOnline(new() { Title = "YouTube Title", GameName = "Coding", ViewerCount = 50 }, DateTimeOffset.UtcNow);

        var messenger = new BroadcastMessenger(new ChatPlatformRegistry([twitch, youtube]), NullLogger<BroadcastMessenger>.Instance);

        var settings = new AppSettings
        {
            Twitch =
            {
                AutoBroadcast =
                {
                    BroadcastMessageTemplate = "{counter}: {title} | {game} | {viewers}",
                },
            },
        };
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        await using var defaultScheduler = new BroadcastScheduler(
            messenger,
            new DefaultBroadcastPlatformTargetsProvider(),
            settingsManager,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            TimeProvider.System,
            NullLogger<BroadcastScheduler>.Instance);

        defaultScheduler.Start("chan");
        await defaultScheduler.ManualSendAsync();

        Assert.Multiple(() =>
        {
            Assert.That(twitch.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "1: Twitch Title | Chess | 100" }));
            Assert.That(youtube.FakeMessenger.Sent, Is.Empty);
            Assert.That(defaultScheduler.SentMessagesCount, Is.EqualTo(1));
        });

        twitch.FakeMessenger.Sent.Clear();

        var dualTargets = Substitute.For<IBroadcastPlatformTargetsProvider>();
        dualTargets.GetTargetPlatforms().Returns(new HashSet<StreamingPlatform> { StreamingPlatform.Twitch, StreamingPlatform.YouTube });

        await using var dualScheduler = new BroadcastScheduler(
            messenger,
            dualTargets,
            settingsManager,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            TimeProvider.System,
            NullLogger<BroadcastScheduler>.Instance);

        dualScheduler.Start("chan");
        await dualScheduler.ManualSendAsync();

        Assert.Multiple(() =>
        {
            Assert.That(twitch.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "1: Twitch Title | Chess | 100" }));
            Assert.That(youtube.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[] { "1: YouTube Title | Coding | 50" }));
            Assert.That(dualScheduler.SentMessagesCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConnectionGreetingAndModeration_RouteToEventPlatform()
    {
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", isConnected: true);
        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", isConnected: true);
        var messenger = new BroadcastMessenger(new ChatPlatformRegistry([twitch, youtube]), NullLogger<BroadcastMessenger>.Instance);

        var settings = new AppSettings
        {
            Twitch =
            {
                Messages =
                {
                    ConnectionEnabled = true,
                    Connection = "Бот онлайн",
                    RewardEnabled = true,
                    RewardMessage = "+{points} {point_term} для {username}",
                },
            },
        };
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var greeting = new ConnectionGreetingHandler(messenger, settingsManager, bus);
        using var moderation = new ModerationChatNotificationHandler(messenger, settingsManager, bus);

        await greeting.HandleAsync(new BotJoinedChannel(StreamingPlatform.YouTube, "yt-chan"), CancellationToken.None);
        await moderation.HandleAsync(new UserRewarded(new(StreamingPlatform.YouTube, "u1"), "Bob", 15, "yt-chan"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(youtube.FakeMessenger.Sent, Has.Count.EqualTo(2));
            Assert.That(youtube.FakeMessenger.Sent[0].Text, Is.EqualTo("Бот онлайн"));
            Assert.That(youtube.FakeMessenger.Sent[1].Text, Does.Contain("Bob").And.Contain("15"));
            Assert.That(twitch.FakeMessenger.Sent, Is.Empty);
        });
    }
}
