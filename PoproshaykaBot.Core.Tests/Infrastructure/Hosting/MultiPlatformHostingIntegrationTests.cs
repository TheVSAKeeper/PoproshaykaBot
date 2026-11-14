using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Hosting;

[TestFixture]
public sealed class MultiPlatformHostingIntegrationTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"PoproshaykaBot_MultiHost_{Guid.NewGuid():N}");
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
    public async Task StartFailure_IsolatesOtherPlatform_AndRollsBackStartedComponentsAndSharedHost()
    {
        using var harness = CreateHarness(failTwitchSecondaryStart: true);
        var platformEvents = new List<PlatformConnectionChanged>();
        using var sub = harness.EventBus.Subscribe<PlatformConnectionChanged>(e => platformEvents.Add(e));

        await ConnectAsync(harness, StreamingPlatform.YouTube);
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.SharedComponent.StartCount, Is.EqualTo(1));
        Assert.That(harness.SharedComponent.StopCount, Is.EqualTo(0));

        harness.AudienceTracker.ForPlatform(StreamingPlatform.YouTube).OnUserMessage("yt-u1", "YouTubeViewer");
        harness.AudienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("tw-u1", "TwitchViewer");
        harness.CommandResponseTracker.Expect(StreamingPlatform.YouTube, "yt-msg", new CommandResponseMark("помощь", CommandResponseTarget.Chat));
        harness.CommandResponseTracker.Expect(StreamingPlatform.Twitch, "tw-msg", new CommandResponseMark("помощь", CommandResponseTarget.Chat));

        await ConnectAsync(harness, StreamingPlatform.Twitch);

        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Failed));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.Manager.CurrentPhase, Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.TwitchPrimaryComponent.StartCount, Is.EqualTo(1));
        Assert.That(harness.TwitchPrimaryComponent.StopCount, Is.EqualTo(1));
        Assert.That(harness.TwitchSecondaryComponent.StartCount, Is.EqualTo(1));
        Assert.That(harness.TwitchSecondaryComponent.StopCount, Is.EqualTo(0));
        Assert.That(harness.TwitchAdapter.EndSessionCount, Is.EqualTo(1));
        Assert.That(harness.SharedComponent.StopCount, Is.EqualTo(0));
        Assert.That(harness.AudienceTracker.ForPlatform(StreamingPlatform.Twitch).ActiveUserCount, Is.EqualTo(0));
        Assert.That(harness.AudienceTracker.ForPlatform(StreamingPlatform.YouTube).ActiveUserCount, Is.EqualTo(1));
        Assert.That(harness.CommandResponseTracker.TryConsume(StreamingPlatform.Twitch, "tw-msg"), Is.Null);
        Assert.That(harness.CommandResponseTracker.TryConsume(StreamingPlatform.YouTube, "yt-msg"), Is.Not.Null);
        Assert.That(platformEvents.Any(e => e.Platform == StreamingPlatform.Twitch && e.Phase == BotLifecyclePhase.Failed && e.Exception != null), Is.True);

        using var soloFailHarness = CreateHarness(failTwitchSecondaryStart: true);
        await ConnectAsync(soloFailHarness, StreamingPlatform.Twitch);

        Assert.That(soloFailHarness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Failed));
        Assert.That(soloFailHarness.Manager.CurrentPhase, Is.EqualTo(BotLifecyclePhase.Failed));
        Assert.That(soloFailHarness.TwitchPrimaryComponent.StartCount, Is.EqualTo(1));
        Assert.That(soloFailHarness.TwitchPrimaryComponent.StopCount, Is.EqualTo(1));
        Assert.That(soloFailHarness.TwitchSecondaryComponent.StopCount, Is.EqualTo(0));
        Assert.That(soloFailHarness.SharedComponent.StartCount, Is.EqualTo(1));
        Assert.That(soloFailHarness.SharedComponent.StopCount, Is.EqualTo(1));
    }

    [Test]
    public async Task OfflineOnOnePlatform_DisconnectsOnlyThatPlatform_AndRespectsCatchUpAndBackOnline()
    {
        using var harness = CreateHarness();
        harness.AutomationSettings.Set(StreamingPlatform.Twitch, new BotLifecycleAutomationSettings
        {
            AutoConnectOnStreamOnline = true,
            AutoDisconnectOnStreamOffline = true
        });
        harness.AutomationSettings.Set(StreamingPlatform.YouTube, new BotLifecycleAutomationSettings
        {
            AutoConnectOnStreamOnline = true,
            AutoDisconnectOnStreamOffline = true
        });

        await ConnectAsync(harness, StreamingPlatform.Twitch);
        await ConnectAsync(harness, StreamingPlatform.YouTube);

        await harness.EventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null), CancellationToken.None);
        await harness.EventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "yt-chan", null), CancellationToken.None);

        await harness.EventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "yt-chan", IsCatchUp: true), CancellationToken.None);
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Connected));

        var eventBus = harness.EventBus;
        using var offlineSub = eventBus.Subscribe<StreamWentOffline>(async (e, ct) =>
        {
            if (e.Platform == StreamingPlatform.Twitch && !e.IsCatchUp)
            {
                await eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null), ct).ConfigureAwait(false);
            }
        });

        await eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan"), CancellationToken.None);
        await WaitUntilAsync(() => !harness.Manager.IsPlatformBusy(StreamingPlatform.Twitch));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Connected));

        await eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "yt-chan"), CancellationToken.None);
        await WaitUntilAsync(() => harness.Manager.GetPhase(StreamingPlatform.YouTube) == BotLifecyclePhase.Disconnected);

        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Disconnected));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.SharedComponent.StopCount, Is.EqualTo(0));
    }

    [Test]
    public async Task StoppingPlatforms_SendsOneFarewellPerPlatform_InAnyOrderBeforeSenderCloses()
    {
        await AssertFarewellsAsync(stopTwitchFirst: true);
        await AssertFarewellsAsync(stopTwitchFirst: false);
    }

    [Test]
    public async Task ConnectingYouTubeFirst_DoesNotStartTwitchBroadcastsOrPollHistoryStore()
    {
        using var harness = CreateHarness();
        harness.SettingsManager.Mutate(s =>
        {
            s.Twitch.AutoBroadcast.AutoBroadcastEnabled = true;
            s.Twitch.AutoBroadcast.StreamStatusNotificationsEnabled = true;
        });

        var aggregatePhases = new List<BotLifecyclePhase>();
        using var sub = harness.EventBus.Subscribe<BotLifecyclePhaseChanged>(e => aggregatePhases.Add(e.Phase));

        await ConnectAsync(harness, StreamingPlatform.YouTube);
        await harness.EventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "yt-chan", new StreamInfo { Title = "YouTube Live" }), CancellationToken.None);

        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Idle));
        Assert.That(aggregatePhases, Is.EqualTo(new[] { BotLifecyclePhase.Connecting, BotLifecyclePhase.Connected }));
        Assert.That(harness.SharedComponent.StartCount, Is.EqualTo(1));
        Assert.That(harness.PollHistoryStore.IsLoaded, Is.False);
        Assert.That(harness.TwitchPrimaryComponent.StartCount, Is.EqualTo(0));
        harness.BroadcastScheduler.DidNotReceiveWithAnyArgs().Start(default!);
        Assert.That(harness.TwitchChat.FakeMessenger.Sent, Is.Empty);
    }

    [Test]
    public async Task ReconnectingTwitchWhileYouTubeRemainsLive_RefreshesTwitchBotIdAndAudienceWithoutStoppingYouTube()
    {
        using var harness = CreateHarness();
        harness.SettingsManager.Mutate(s =>
        {
            s.Twitch.AutoBroadcast.AutoBroadcastEnabled = true;
            s.Twitch.AutoBroadcast.StreamStatusNotificationsEnabled = false;
        });

        var helix = harness.Helix;
        SetupAuthenticatedUsers(helix, CreateUser("bot-1"), CreateUser("bot-2"));

        await ConnectAsync(harness, StreamingPlatform.YouTube);
        await ConnectAsync(harness, StreamingPlatform.Twitch);
        await harness.EventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", new StreamInfo { Title = "Live" }), CancellationToken.None);

        Assert.That(await harness.BotUserIdProvider.GetAsync(CancellationToken.None), Is.EqualTo("bot-1"));
        Assert.That(await harness.BotUserIdProvider.GetAsync(CancellationToken.None), Is.EqualTo("bot-1"));
        await helix.Received(1).GetAuthenticatedUserAsync(Arg.Any<CancellationToken>());

        await harness.EventBus.PublishAsync(CreateRawMessage(StreamingPlatform.Twitch, "twitch-chan", "tw-u1", "TwitchViewer"), CancellationToken.None);
        await harness.EventBus.PublishAsync(CreateRawMessage(StreamingPlatform.YouTube, "yt-chan", "yt-u1", "YouTubeViewer"), CancellationToken.None);
        harness.CommandResponseTracker.Expect(StreamingPlatform.Twitch, "tw-msg", new CommandResponseMark("помощь", CommandResponseTarget.Chat));
        harness.CommandResponseTracker.Expect(StreamingPlatform.YouTube, "yt-msg", new CommandResponseMark("помощь", CommandResponseTarget.Chat));

        await harness.Manager.StopAsync(StreamingPlatform.Twitch, BotStopMode.Graceful);

        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Disconnected));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(harness.SharedComponent.StopCount, Is.EqualTo(0));
        harness.BroadcastScheduler.Received(1).Stop();
        Assert.That(harness.AudienceTracker.ForPlatform(StreamingPlatform.Twitch).ActiveUserCount, Is.EqualTo(0));
        Assert.That(harness.AudienceTracker.ForPlatform(StreamingPlatform.YouTube).ActiveUserCount, Is.EqualTo(1));
        Assert.That(harness.CommandResponseTracker.TryConsume(StreamingPlatform.Twitch, "tw-msg"), Is.Null);

        await ConnectAsync(harness, StreamingPlatform.Twitch);

        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(await harness.BotUserIdProvider.GetAsync(CancellationToken.None), Is.EqualTo("bot-2"));
        await helix.Received(2).GetAuthenticatedUserAsync(Arg.Any<CancellationToken>());
        Assert.That(harness.CommandResponseTracker.TryConsume(StreamingPlatform.YouTube, "yt-msg"), Is.Not.Null);
        Assert.That(harness.AudienceTracker.ForPlatform(StreamingPlatform.YouTube).ActiveUserCount, Is.EqualTo(1));

        await harness.Manager.ShutdownAsync();
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.Twitch), Is.EqualTo(BotLifecyclePhase.Disconnected));
        Assert.That(harness.Manager.GetPhase(StreamingPlatform.YouTube), Is.EqualTo(BotLifecyclePhase.Disconnected));
        Assert.That(harness.SharedComponent.StopCount, Is.EqualTo(1));
    }

    private async Task AssertFarewellsAsync(bool stopTwitchFirst)
    {
        using var harness = CreateHarness();
        harness.SettingsManager.Mutate(s => s.Twitch.Messages.FarewellEnabled = true);

        await ConnectAsync(harness, StreamingPlatform.Twitch);
        await ConnectAsync(harness, StreamingPlatform.YouTube);

        await harness.EventBus.PublishAsync(CreateRawMessage(StreamingPlatform.Twitch, "twitch-chan", "tw-u1", "TwAlice"), CancellationToken.None);
        await harness.EventBus.PublishAsync(CreateRawMessage(StreamingPlatform.YouTube, "yt-chan", "yt-u1", "YtBob"), CancellationToken.None);

        var first = stopTwitchFirst ? StreamingPlatform.Twitch : StreamingPlatform.YouTube;
        var second = stopTwitchFirst ? StreamingPlatform.YouTube : StreamingPlatform.Twitch;

        await harness.Manager.StopAsync(first, BotStopMode.Graceful);
        await harness.Manager.StopAsync(first, BotStopMode.Graceful);

        if (first == StreamingPlatform.Twitch)
        {
            Assert.That(harness.TwitchChat.FakeMessenger.Sent, Has.Count.EqualTo(1));
            Assert.That(harness.YouTubeChat.FakeMessenger.Sent, Is.Empty);
        }
        else
        {
            Assert.That(harness.YouTubeChat.FakeMessenger.Sent, Has.Count.EqualTo(1));
            Assert.That(harness.TwitchChat.FakeMessenger.Sent, Is.Empty);
        }

        await harness.Manager.StopAsync(second, BotStopMode.Graceful);

        Assert.That(harness.TwitchChat.FakeMessenger.Sent, Has.Count.EqualTo(1));
        Assert.That(harness.YouTubeChat.FakeMessenger.Sent, Has.Count.EqualTo(1));
        Assert.That(harness.TwitchChat.FakeMessenger.Sent[0].Text, Does.Contain("TwAlice").And.Not.Contain("YtBob"));
        Assert.That(harness.YouTubeChat.FakeMessenger.Sent[0].Text, Does.Contain("YtBob").And.Not.Contain("TwAlice"));
    }

    private static Task ConnectAsync(TestHarness harness, StreamingPlatform platform)
    {
        harness.Manager.StartConnection(platform);
        return harness.Manager.WaitForConnectionAsync(platform);
    }

    private static void SetupAuthenticatedUsers(ITwitchHelixClient helix, UserInfo first, UserInfo second)
    {
        var call = 0;
        helix.GetAuthenticatedUserAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<UserInfo?>(Interlocked.Increment(ref call) == 1 ? first : second));
    }

    private TestHarness CreateHarness(bool failTwitchSecondaryStart = false)
    {
        var caseRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caseRoot);

        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(caseRoot, "settings.json"));
        var obsChatStore = new ObsChatStore(eventBus, NullLogger<ObsChatStore>.Instance, Path.Combine(caseRoot, "obs-chat.json"));
        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(caseRoot, "commands.json"));
        var pollsStore = new PollsStore(NullLogger<PollsStore>.Instance, Path.Combine(caseRoot, "polls.json"));

        var channels = new PlatformChannelRegistry();
        var twitchChat = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", channels: channels);
        var youtubeChat = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", channels: channels);
        var platformRegistry = new ChatPlatformRegistry([twitchChat, youtubeChat]);

        var broadcastMessenger = new BroadcastMessenger(platformRegistry, NullLogger<BroadcastMessenger>.Instance);
        var audienceTracker = new AudienceTracker(settingsManager);
        var responseTracker = new CommandResponseTracker(TimeProvider.System);
        var commandProcessor = new ChatCommandProcessor(
            [],
            commandSettingsStore,
            new CommandUsageRepository(TimeProvider.System),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: platformRegistry);
        var chatPipeline = new ChatPipelineHandler(
            platformRegistry,
            channels,
            settingsManager,
            obsChatStore,
            commandSettingsStore,
            audienceTracker,
            commandProcessor,
            responseTracker,
            eventBus,
            NullLogger<ChatPipelineHandler>.Instance);

        var helix = Substitute.For<ITwitchHelixClient>();
        var broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("broadcaster-1");
        var targetChannelProvider = Substitute.For<ITargetChannelProvider>();
        targetChannelProvider.Current.Returns(new TargetChannelState("twitch-chan", "twitch-chan", IsDebugSession: false, IsSendingAllowed: true));

        var pollHistoryStore = new PollHistoryStore(
            pollsStore,
            helix,
            broadcasterIdProvider,
            targetChannelProvider,
            NullLogger<PollHistoryStore>.Instance,
            Path.Combine(caseRoot, "polls-history.json"));

        var sharedComponent = new RecordingHostedComponent("Shared", 10);
        var twitchPrimaryComponent = new RecordingHostedComponent("TwitchPrimary", 10);
        var twitchSecondaryComponent = new RecordingHostedComponent(
            "TwitchSecondary",
            20,
            failStart: failTwitchSecondaryStart);
        var youtubeComponent = new RecordingHostedComponent("YouTubePrimary", 10);

        var sharedHost = new AppHost([sharedComponent], NullLogger<AppHost>.Instance);
        var twitchHost = new PlatformHost(
            StreamingPlatform.Twitch,
            [twitchPrimaryComponent, pollHistoryStore, twitchSecondaryComponent],
            NullLogger<PlatformHost>.Instance);
        var youtubeHost = new PlatformHost(
            StreamingPlatform.YouTube,
            [youtubeComponent],
            NullLogger<PlatformHost>.Instance);

        var twitchAdapter = new FakePlatformConnectionAdapter(StreamingPlatform.Twitch, twitchHost, channels, "twitch-chan");
        var youtubeAdapter = new FakePlatformConnectionAdapter(StreamingPlatform.YouTube, youtubeHost, channels, "yt-chan");

        var manager = new BotConnectionManager(
            sharedHost,
            [twitchAdapter, youtubeAdapter],
            chatPipeline,
            eventBus,
            NullLogger<BotConnectionManager>.Instance);

        var scheduler = Substitute.For<IBroadcastScheduler>();
        var isSchedulerActive = false;
        scheduler.IsActive.Returns(_ => isSchedulerActive);
        scheduler.When(x => x.Start(Arg.Any<string>())).Do(_ => isSchedulerActive = true);
        scheduler.When(x => x.Stop()).Do(_ => isSchedulerActive = false);

        var streamStatus = Substitute.For<IStreamStatus>();
        streamStatus.CurrentStatus.Returns(StreamStatus.Offline);

        var automationSettings = new FakeBotLifecycleAutomationSettingsProvider();
        var farewellHandler = new FarewellMessageHandler(
            platformRegistry,
            audienceTracker,
            broadcastMessenger,
            settingsManager,
            eventBus,
            NullLogger<FarewellMessageHandler>.Instance);
        var streamBroadcastHandler = new StreamStatusBroadcastHandler(
            scheduler,
            broadcastMessenger,
            streamStatus,
            targetChannelProvider,
            settingsManager,
            eventBus,
            NullLogger<StreamStatusBroadcastHandler>.Instance);
        var automationHandler = new BotLifecycleAutomationHandler(
            manager,
            automationSettings,
            eventBus,
            NullLogger<BotLifecycleAutomationHandler>.Instance);
        var botUserIdProvider = new BotUserIdProvider(helix, eventBus, NullLogger<BotUserIdProvider>.Instance);

        return new TestHarness(
            eventBus,
            settingsManager,
            channels,
            twitchChat,
            youtubeChat,
            audienceTracker,
            responseTracker,
            chatPipeline,
            pollHistoryStore,
            sharedComponent,
            twitchPrimaryComponent,
            twitchSecondaryComponent,
            youtubeComponent,
            twitchAdapter,
            youtubeAdapter,
            manager,
            helix,
            scheduler,
            automationSettings,
            farewellHandler,
            streamBroadcastHandler,
            automationHandler,
            botUserIdProvider);
    }

    private static RawChatMessageReceived CreateRawMessage(
        StreamingPlatform platform,
        string channel,
        string userId,
        string displayName)
    {
        return new RawChatMessageReceived(
            new ChatMessage(
                platform,
                channel,
                Guid.NewGuid().ToString("N"),
                userId,
                displayName.ToLowerInvariant(),
                displayName,
                "привет",
                [],
                [],
                IsBroadcaster: false,
                IsModerator: false,
                IsVip: false,
                IsSubscriber: false),
            DateTimeOffset.UtcNow);
    }

    private static UserInfo CreateUser(string id)
    {
        return new UserInfo(
            id,
            "bot",
            "Bot",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            DateTime.UtcNow);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class TestHarness(
        InMemoryEventBus eventBus,
        SettingsManager settingsManager,
        PlatformChannelRegistry channels,
        FakeChatPlatform twitchChat,
        FakeChatPlatform youtubeChat,
        AudienceTracker audienceTracker,
        CommandResponseTracker commandResponseTracker,
        ChatPipelineHandler chatPipeline,
        PollHistoryStore pollHistoryStore,
        RecordingHostedComponent sharedComponent,
        RecordingHostedComponent twitchPrimaryComponent,
        RecordingHostedComponent twitchSecondaryComponent,
        RecordingHostedComponent youtubeComponent,
        FakePlatformConnectionAdapter twitchAdapter,
        FakePlatformConnectionAdapter youtubeAdapter,
        BotConnectionManager manager,
        ITwitchHelixClient helix,
        IBroadcastScheduler broadcastScheduler,
        FakeBotLifecycleAutomationSettingsProvider automationSettings,
        FarewellMessageHandler farewellHandler,
        StreamStatusBroadcastHandler streamBroadcastHandler,
        BotLifecycleAutomationHandler automationHandler,
        BotUserIdProvider botUserIdProvider) : IDisposable
    {
        public InMemoryEventBus EventBus { get; } = eventBus;
        public SettingsManager SettingsManager { get; } = settingsManager;
        public PlatformChannelRegistry Channels { get; } = channels;
        public FakeChatPlatform TwitchChat { get; } = twitchChat;
        public FakeChatPlatform YouTubeChat { get; } = youtubeChat;
        public AudienceTracker AudienceTracker { get; } = audienceTracker;
        public CommandResponseTracker CommandResponseTracker { get; } = commandResponseTracker;
        public ChatPipelineHandler ChatPipeline { get; } = chatPipeline;
        public PollHistoryStore PollHistoryStore { get; } = pollHistoryStore;
        public RecordingHostedComponent SharedComponent { get; } = sharedComponent;
        public RecordingHostedComponent TwitchPrimaryComponent { get; } = twitchPrimaryComponent;
        public RecordingHostedComponent TwitchSecondaryComponent { get; } = twitchSecondaryComponent;
        public RecordingHostedComponent YouTubeComponent { get; } = youtubeComponent;
        public FakePlatformConnectionAdapter TwitchAdapter { get; } = twitchAdapter;
        public FakePlatformConnectionAdapter YouTubeAdapter { get; } = youtubeAdapter;
        public BotConnectionManager Manager { get; } = manager;
        public ITwitchHelixClient Helix { get; } = helix;
        public IBroadcastScheduler BroadcastScheduler { get; } = broadcastScheduler;
        public FakeBotLifecycleAutomationSettingsProvider AutomationSettings { get; } = automationSettings;

        private readonly FarewellMessageHandler _farewellHandler = farewellHandler;
        private readonly StreamStatusBroadcastHandler _streamBroadcastHandler = streamBroadcastHandler;
        private readonly BotLifecycleAutomationHandler _automationHandler = automationHandler;
        public BotUserIdProvider BotUserIdProvider { get; } = botUserIdProvider;

        public void Dispose()
        {
            _farewellHandler.Dispose();
            _streamBroadcastHandler.Dispose();
            _automationHandler.Dispose();
            BotUserIdProvider.Dispose();
            ChatPipeline.Dispose();
        }
    }

    private sealed class RecordingHostedComponent(
        string name,
        int startOrder,
        bool failStart = false) : IHostedComponent
    {
        public string Name => name;

        public int StartOrder => startOrder;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            StartCount++;

            if (failStart)
            {
                throw new InvalidOperationException($"{name} start failed");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlatformConnectionAdapter(
        StreamingPlatform platform,
        PlatformHost host,
        PlatformChannelRegistry channels,
        string channel) : IPlatformConnectionAdapter
    {
        public StreamingPlatform Platform => platform;

        public bool IsEnabled => true;

        public PlatformHost Host => host;

        public int EndSessionCount { get; private set; }

        public Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            channels.SetChannel(platform, channel);
            return Task.FromResult(channel);
        }

        public void EndSession()
        {
            EndSessionCount++;
            channels.Clear(platform);
        }
    }

    private sealed class FakeBotLifecycleAutomationSettingsProvider : IBotLifecycleAutomationSettingsProvider
    {
        private readonly Dictionary<StreamingPlatform, BotLifecycleAutomationSettings> _settings = new();

        public void Set(StreamingPlatform platform, BotLifecycleAutomationSettings settings)
        {
            _settings[platform] = settings;
        }

        public BotLifecycleAutomationSettings Get(StreamingPlatform platform)
        {
            return _settings.GetValueOrDefault(platform) ?? new BotLifecycleAutomationSettings();
        }
    }
}
