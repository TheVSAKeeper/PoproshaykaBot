using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Platforms;

[TestFixture]
public sealed class ThirdPlatformPipelineTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-3p-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task VkLive_runs_commands_channel_switch_stats_broadcast_history_and_reports_missing_monitoring()
    {
        var time = new ManualTimeProvider();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_root, "channels.json"), gate);
        var vkChanA = new ChannelKey(StreamingPlatform.VkLive, "vk-alpha");
        var vkChanB = new ChannelKey(StreamingPlatform.VkLive, "vk-beta");
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.VkLive, ChannelId = vkChanA.ChannelId, DisplayName = "AlphaVK", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.VkLive, ChannelId = vkChanB.ChannelId, DisplayName = "BetaVK", IsConfigured = true, MonitoringEnabled = true },
            ];
            s.Platforms = [new() { Platform = StreamingPlatform.VkLive, ActiveChannelId = vkChanA.ChannelId, SwitchMode = ChannelSwitchMode.Manual }];
        });

        var channelRegistry = new PlatformChannelRegistry();
        channelRegistry.SetBinding(new(vkChanA, "AlphaVK", AuthContextIds.Default, AuthContextIds.Default, 0));

        var vkChat = new FakeChatPlatform(
            StreamingPlatform.VkLive,
            "VK Live",
            PlatformCapabilities.None,
            botIdentity: new("vk-bot", "VkBot"),
            channels: channelRegistry);
        var platformRegistry = new ChatPlatformRegistry([vkChat]);

        var thirdAdapter = new ThirdPlatformTestAdapter(
            StreamingPlatform.VkLive,
            PlatformCapabilities.None,
            bus,
            channelRegistry);

        var settings = new AppSettings();
        settings.Twitch.Messages.WelcomeEnabled = false;
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var viewerKey = new PlatformUserKey(StreamingPlatform.VkLive, "vk-viewer-1");
        repo.TrackMessage(vkChanA, viewerKey, "VKViewer");
        repo.IncrementBonusPoints(StatisticsCounterTarget.ForChannel(vkChanA), viewerKey, 49, "VKViewer");
        var rankService = new UserRankService(settingsManager);

        IReadOnlyCollection<IChatCommand>? commandList = null;
        var help = new HelpCommand(_ => commandList!, "!");
        var rank = new RankCommand(repo, rankService, catalog);
        var top = new TopUsersCommand(repo, rankService, catalog);
        commandList = [help, rank, top];

        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_root, "obs.json"));
        var processor = new ChatCommandProcessor(
            commandList,
            commandSettingsStore,
            new(time),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: platformRegistry);

        using var chatPipeline = new ChatPipelineHandler(
            platformRegistry,
            channelRegistry,
            settingsManager,
            obsChatStore,
            commandSettingsStore,
            new AudienceTracker(settingsManager),
            processor,
            new(time),
            bus,
            NullLogger<ChatPipelineHandler>.Instance);

        await chatPipeline.HandleAsync(CreateVkRaw(vkChanA, "m1", "!помощь", time.GetUtcNow()), CancellationToken.None);
        await chatPipeline.HandleAsync(CreateVkRaw(vkChanA, "m2", "!ранг", time.GetUtcNow()), CancellationToken.None);
        await chatPipeline.HandleAsync(CreateVkRaw(vkChanA, "m3", "!top", time.GetUtcNow()), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(vkChat.FakeMessenger.Sent, Has.Count.EqualTo(3));
            Assert.That(vkChat.FakeMessenger.Sent.Any(m => m.Text.Contains("помощь", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(vkChat.FakeMessenger.Sent.Any(m => m.Text.Contains("50", StringComparison.Ordinal)), Is.True);
            Assert.That(vkChat.FakeMessenger.Sent.Any(m => m.Text.Contains("VKViewer", StringComparison.Ordinal)), Is.True);
            Assert.That(vkChat.FakeMessenger.Replies, Is.Empty);
            Assert.That(vkChat.FakeMessenger.Whispers, Is.Empty);
        });

        var monitoringEvents = new List<ChannelMonitoringStateChanged>();
        using var monSub = bus.Subscribe<ChannelMonitoringStateChanged>(e => monitoringEvents.Add(e));
        thirdAdapter.PublishUnsupportedMonitoring(vkChanA, "AlphaVK");
        Assert.That(monitoringEvents[^1].Snapshot.Reason, Is.EqualTo(MonitoringUnavailableReason.NotSupportedByPlatform));

        var history = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(_root, "broadcasts.json"),
            Path.Combine(_root, "sessions.json"));
        var active = new ActiveBroadcastStore(
            history,
            NullLogger<ActiveBroadcastStore>.Instance,
            Path.Combine(_root, "active_b.json"),
            Path.Combine(_root, "active_s.json"));
        var target = new FakeTargetChannelProvider("AlphaVK");
        var drafts = new BroadcastDraftCoordinator(history, active, target, time, NullLogger<BroadcastDraftCoordinator>.Instance);
        using var aggregator = new BroadcastAggregator(bus, drafts, target, time, NullLogger<BroadcastAggregator>.Instance);
        await using var statsHandler = new StreamSessionStatisticsHandler(
            Substitute.For<IBroadcastMessenger>(),
            aggregator,
            drafts,
            target,
            settingsManager,
            time,
            bus,
            active,
            NullLogger<StreamSessionStatisticsHandler>.Instance);

        var completed = new List<BroadcastRecord>();
        using var compSub = bus.Subscribe<StreamSessionCompleted>(e => completed.Add(e.Broadcast));

        var appHost = new AppHost([], NullLogger<AppHost>.Instance);
        await using var connManager = new BotConnectionManager(appHost, [thirdAdapter], chatPipeline, bus, NullLogger<BotConnectionManager>.Instance);
        connManager.StartConnection();
        await connManager.WaitForConnectionAsync();

        var reconciler = new ChannelCatalogReconciler(
            catalog,
            settingsManager,
            new AccountsStore(null, Path.Combine(_root, "tw.json"), gate),
            new YouTubeSettingsStore(null, Path.Combine(_root, "yt.json"), gate),
            new YouTubeAccountsStore(null, Path.Combine(_root, "ytacc.json"), gate),
            target,
            NullLogger<ChannelCatalogReconciler>.Instance);
        var auth = Substitute.For<IPlatformAuthService>();
        auth.Platform.Returns(StreamingPlatform.VkLive);
        auth.GetAccessTokenAsync(Arg.Any<PlatformAuthRole>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("vk-bot-tok"));
        auth.GetAccessTokenAsync(Arg.Any<PlatformAuthRole>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("vk-bot-tok"));

        using var switchCoordinator = new ChannelSwitchCoordinator(
            catalog,
            channelRegistry,
            reconciler,
            new PlatformAuthRegistry([auth]),
            aggregator,
            [thirdAdapter],
            [thirdAdapter],
            target,
            bus,
            time,
            NullLogger<ChannelSwitchCoordinator>.Instance,
            connManager,
            chatPipeline);

        thirdAdapter.LiveProbes[vkChanB] = new(true, true, new() { Id = "vk-live-b", StartedAt = time.GetUtcNow().UtcDateTime, ViewerCount = 18 });
        var switched = await switchCoordinator.SwitchActiveChannelAsync(StreamingPlatform.VkLive, vkChanB);
        Assert.That(switched.Succeeded, Is.True);
        Assert.That(channelRegistry.GetBinding(StreamingPlatform.VkLive)?.Key, Is.EqualTo(vkChanB));

        time.Advance(TimeSpan.FromMinutes(5));
        await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.VkLive, "BetaVK", ChannelId: vkChanB.ChannelId));

        for (var i = 0; i < 60 && completed.Count == 0; i++)
        {
            await Task.Delay(25);
        }

        Assert.That(completed, Has.Count.EqualTo(1));
        Assert.That(completed[0].Platforms.Any(c => c.Platform == StreamingPlatform.VkLive && c.ChannelId == vkChanB.ChannelId), Is.True);
    }

    private static RawChatMessageReceived CreateVkRaw(ChannelKey channelKey, string messageId, string text, DateTimeOffset receivedAt)
    {
        return new(
            new(
                StreamingPlatform.VkLive,
                "AlphaVK",
                messageId,
                "vk-viewer-1",
                "vkviewer",
                "VKViewer",
                text,
                [],
                [],
                false,
                false,
                false,
                false,
                false,
                ChannelId: channelKey.ChannelId,
                Origin: new(channelKey, "AlphaVK")),
            receivedAt);
    }
}
