using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Collections.Concurrent;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeStage7PipelineIntegrationTests
{
    private const string ChannelId = "UCbroadcaster00000000007";
    private const string BotId = "UCbot0000000000000000007";

    private string _directory = null!;
    private ManualTimeProvider _clock = null!;
    private YouTubeSettingsStore _youtubeSettingsStore = null!;
    private YouTubeAccountsStore _youtubeAccountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;
    private CommandSettingsStore _commandSettingsStore = null!;
    private SettingsManager _settingsManager = null!;
    private ObsChatStore _obsChatStore = null!;
    private InMemoryEventBus _eventBus = null!;
    private YouTubeSessionContext _sessionContext = null!;
    private YouTubeStreamMonitor _streamMonitor = null!;
    private YouTubeSentMessageTracker _sentTracker = null!;
    private CommandResponseTracker _responseTracker = null!;
    private FakeChatPlatform _twitch = null!;
    private YouTubeChatPlatform _youtube = null!;
    private ChatPlatformRegistry _registry = null!;
    private BroadcastMessenger _broadcastMessenger = null!;
    private YouTubeMessenger _youtubeMessenger = null!;
    private AudienceTracker _audienceTracker = null!;
    private ChatCommandProcessor _commandProcessor = null!;
    private ChatPipelineHandler _pipeline = null!;

    private ConcurrentQueue<(string Text, YouTubeQuotaPriority Priority)> _sentToYouTube = null!;
    private int _nextSentId;

    [SetUp]
    public async Task SetUpAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-stage7-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));
        _sentToYouTube = new();
        _nextSentId = 0;

        _youtubeSettingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _youtubeSettingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj-stage7", DailyQuotaBudget = 10_000, Channel = ChannelId });

        _youtubeAccountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _youtubeAccountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = BotId, ChannelTitle = "BotYT" },
            new YouTubeAccountSettings { ChannelId = ChannelId, ChannelTitle = "StreamerYT" });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _youtubeSettingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);

        _commandSettingsStore = new CommandSettingsStore(NullLogger<CommandSettingsStore>.Instance, Path.Combine(_directory, "commands.json"));

        var appSettings = new AppSettings
        {
            Twitch =
            {
                Channel = "twitch_chan",
                Messages =
                {
                    WelcomeEnabled = true,
                    Welcome = "Привет, {username}!",
                    ConnectionEnabled = true,
                    Connection = "Бот вошёл в чат",
                    FarewellEnabled = true,
                    Farewell = "Пока, {username}!",
                    DisconnectionEnabled = true,
                    Disconnection = "Бот уходит",
                },
                AutoBroadcast =
                {
                    AutoBroadcastEnabled = true,
                    BroadcastMessageTemplate = "Автосообщение #{counter}",
                    BroadcastIntervalMinutes = 10,
                },
            },
        };
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        _settingsManager.Current.Returns(appSettings);

        _eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        _obsChatStore = new ObsChatStore(_eventBus, NullLogger<ObsChatStore>.Instance, Path.Combine(_directory, "obs-chat.json"));

        var broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("vid-7", ChannelId, "StreamerYT", "chat-7", "Stream Title", _clock.GetUtcNow())]);

        var authService = new StubAuthService();

        _sessionContext = new YouTubeSessionContext();
        _sessionContext.Begin(new(ChannelId, ChannelId, BotId, "BotYT"));

        var resolver = new YouTubeTargetChannelResolver(broadcastClient, _youtubeAccountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        _streamMonitor = new YouTubeStreamMonitor(
            broadcastClient,
            resolver,
            new InMemoryYouTubeBroadcastSelectionStore(),
            authService,
            _youtubeSettingsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance);
        await _streamMonitor.RefreshLiveSnapshotAsync();

        _sentTracker = new YouTubeSentMessageTracker();
        _responseTracker = new CommandResponseTracker(_clock);

        var targetProvider = Substitute.For<ITargetChannelProvider>();
        targetProvider.Current.Returns(new TargetChannelState(ChannelId, ChannelId, IsDebugSession: false, IsSendingAllowed: true));

        var sendClient = new StubSendClient((_, text, priority, _) =>
        {
            var id = $"yt-sent-{Interlocked.Increment(ref _nextSentId)}";
            _sentToYouTube.Enqueue((text, priority));
            return Task.FromResult(new YouTubeInsertMessageResult(id, _clock.GetUtcNow()));
        });

        _youtubeMessenger = new YouTubeMessenger(
            sendClient,
            _streamMonitor,
            targetProvider,
            _quotaGovernor,
            _sentTracker,
            _responseTracker,
            _clock,
            NullLogger<YouTubeMessenger>.Instance);
        await _youtubeMessenger.StartAsync(new Progress<string>(), CancellationToken.None);

        _twitch = new FakeChatPlatform(
            StreamingPlatform.Twitch,
            "Twitch",
            PlatformCapabilities.Reply | PlatformCapabilities.Whisper | PlatformCapabilities.ChannelMetadataEdit,
            isConnected: true);
        _youtube = new YouTubeChatPlatform(
            _streamMonitor,
            _sessionContext,
            _streamMonitor,
            _youtubeMessenger,
            _youtubeSettingsStore,
            _youtubeAccountsStore,
            targetProvider);

        _registry = new ChatPlatformRegistry([_twitch, _youtube]);
        _broadcastMessenger = new BroadcastMessenger(_registry, NullLogger<BroadcastMessenger>.Instance);
        _audienceTracker = new AudienceTracker(_settingsManager);

        var applier = Substitute.For<IChannelInformationApplier>();
        var gameResolver = Substitute.For<IGameCategoryResolver>();

        var commands = new IChatCommand[]
        {
            new TestTextCommand("пинг", "понг"),
            new TitleCommand(applier, NullLogger<TitleCommand>.Instance),
            new GameCommand(applier, gameResolver, NullLogger<GameCommand>.Instance),
            new ProfileCommand(null!, NullLogger<ProfileCommand>.Instance),
        };

        _commandProcessor = new ChatCommandProcessor(
            commands,
            _commandSettingsStore,
            new CommandUsageRepository(_clock),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: _registry);
        _commandProcessor.Register(new HelpCommand(_commandProcessor.GetAvailableCommands, _commandProcessor.Prefix));

        _pipeline = new ChatPipelineHandler(
            _registry,
            new PlatformChannelRegistry(),
            _settingsManager,
            _obsChatStore,
            _commandSettingsStore,
            _audienceTracker,
            _commandProcessor,
            _responseTracker,
            _eventBus,
            NullLogger<ChatPipelineHandler>.Instance);
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        _pipeline.Dispose();
        await _youtubeMessenger.StopAsync(new Progress<string>(), CancellationToken.None);
        _youtubeMessenger.Dispose();
        _streamMonitor.Dispose();
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Commands_Deliver_To_YouTube_With_Table24_Rules_And_Hide_Metadata_Without_UnknownCommandsFile()
    {
        _commandSettingsStore.Mutate(s =>
        {
            s.DefaultResponseTarget = CommandResponseTarget.Chat;
            s.Commands["пинг"] = new() { ResponseTarget = CommandResponseTarget.Caller };
            s.Commands["помощь"] = new() { ResponseTarget = CommandResponseTarget.Whisper };
            s.Commands["оверлей"] = new() { ResponseTarget = CommandResponseTarget.Overlay };
        });
        _commandProcessor.Register(new TestTextCommand("оверлей", "только оверлей"));

        var chatEvents = new List<ChatMessageReceived>();
        using var chatSub = _eventBus.Subscribe<ChatMessageReceived>(chatEvents.Add);

        var unknownFile = AppPaths.Combine("unknown_commands.txt");
        var unknownSizeBefore = File.Exists(unknownFile) ? new FileInfo(unknownFile).Length : 0;

        // First viewer invokes Caller command -> welcome merged with response, single send
        await PublishYouTubeViewerMessageAsync("m-1", "UCv1", "Alice", "!пинг");
        _clock.Advance(TimeSpan.FromSeconds(2));

        // Second viewer invokes Whisper command -> falls back to @Bob mention, hides !title/!game/!пресет
        await PublishYouTubeViewerMessageAsync("m-2", "UCv2", "Bob", "!помощь");
        _clock.Advance(TimeSpan.FromSeconds(2));

        // Third viewer invokes Overlay-only command -> standalone welcome sent to chat, command reply published locally with YouTube BotIdentity
        await PublishYouTubeViewerMessageAsync("m-3", "UCv3", "Carol", "!оверлей");
        _clock.Advance(TimeSpan.FromSeconds(2));

        // Moderator invokes Twitch-only metadata commands -> rejected, NOT logged to unknown_commands.txt
        await PublishYouTubeViewerMessageAsync("m-4", "UCmod1", "Mod", "!title Новое", isModerator: true);
        await PublishYouTubeViewerMessageAsync("m-5", "UCmod1", "Mod", "!game Chess", isModerator: true);
        await PublishYouTubeViewerMessageAsync("m-6", "UCmod1", "Mod", "!пресет 1", isModerator: true);

        await WaitForSentCountAsync(4);
        var unknownSizeAfter = File.Exists(unknownFile) ? new FileInfo(unknownFile).Length : 0;

        var sent = _sentToYouTube.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(_twitch.FakeMessenger.Sent, Is.Empty);
            Assert.That(_twitch.FakeMessenger.Replies, Is.Empty);
            Assert.That(unknownSizeAfter, Is.EqualTo(unknownSizeBefore));

            Assert.That(sent[0].Text, Is.EqualTo("@Alice Привет, Alice! понг"));
            Assert.That(sent[0].Priority, Is.EqualTo(YouTubeQuotaPriority.CommandReply));

            Assert.That(sent[1].Text, Is.EqualTo("Привет, Bob!"));
            Assert.That(sent[1].Priority, Is.EqualTo(YouTubeQuotaPriority.StandaloneGreeting));

            Assert.That(sent[2].Text, Does.StartWith("@Bob 📋 Команды:"));
            Assert.That(sent[2].Text, Does.Not.Contain("!title").And.Not.Contain("!game").And.Not.Contain("!пресет"));
            Assert.That(sent[2].Priority, Is.EqualTo(YouTubeQuotaPriority.CommandReply));

            Assert.That(sent[3].Text, Is.EqualTo("Привет, Carol!"));
            Assert.That(sent[3].Priority, Is.EqualTo(YouTubeQuotaPriority.StandaloneGreeting));

            var overlayReply = chatEvents.Single(e => e.Platform == StreamingPlatform.YouTube && e.IsBot && e.Text == "только оверлей");
            Assert.That(overlayReply.UserId, Is.EqualTo(BotId));
            Assert.That(overlayReply.DisplayName, Is.EqualTo("BotYT"));
        });
    }

    [Test]
    public async Task Quota_Cascade_Disables_Scheduled_At_5500_Greetings_At_7000_And_Commands_At_8000_While_Reading_Continues()
    {
        // Consume 5,500 units -> ceiling for ScheduledBroadcast (4) on 10,000 budget is 5,500
        Assert.That(_quotaGovernor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, 5_500, out var lease1), Is.True);
        lease1.Commit();

        var targetsProvider = Substitute.For<IBroadcastPlatformTargetsProvider>();
        targetsProvider.GetTargetPlatforms().Returns(new HashSet<StreamingPlatform> { StreamingPlatform.YouTube });

        await using var scheduler = new BroadcastScheduler(
            _broadcastMessenger,
            targetsProvider,
            _settingsManager,
            _eventBus,
            _clock,
            NullLogger<BroadcastScheduler>.Instance);
        using var greetingHandler = new ConnectionGreetingHandler(_broadcastMessenger, _settingsManager, _eventBus);
        using var farewellHandler = new FarewellMessageHandler(_registry, _audienceTracker, _broadcastMessenger, _settingsManager, _eventBus, NullLogger<FarewellMessageHandler>.Instance);

        // At 5,500: BroadcastScheduler (4) is blocked, ConnectionGreeting (3) and CommandReply (2) pass
        scheduler.Start(ChannelId);
        await scheduler.ManualSendAsync();
        await greetingHandler.HandleAsync(new(StreamingPlatform.YouTube, "chat-7"), CancellationToken.None);
        await PublishYouTubeViewerMessageAsync("q-1", "UCq1", "User1", "!пинг");
        _clock.Advance(TimeSpan.FromSeconds(2));
        await WaitForSentCountAsync(2);

        // Advance committed units to 7,000 -> ceiling for StandaloneGreeting (3) is 7,000
        var remainingTo7000 = 7_000 - _quotaGovernor.CaptureDiagnostics().CommittedUnits;
        Assert.That(_quotaGovernor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, remainingTo7000, out var lease2), Is.True);
        lease2.Commit();

        // At 7,000: standalone farewell / standalone greeting (3) is blocked, but command reply with merged welcome (2) still passes!
        var farewellResult = _broadcastMessenger.Send(StreamingPlatform.YouTube, "Пока!", ChatSendCategory.Greeting);
        await PublishYouTubeViewerMessageAsync("q-2", "UCq2", "User2", "просто сообщение"); // standalone welcome only
        await PublishYouTubeViewerMessageAsync("q-3", "UCq3", "User3", "!пинг"); // merged welcome + command reply
        _clock.Advance(TimeSpan.FromSeconds(2));
        await WaitForSentCountAsync(3);

        // Advance committed units to 8,000 -> ceiling for CommandReply (2) is 8,000
        var remainingTo8000 = 8_000 - _quotaGovernor.CaptureDiagnostics().CommittedUnits;
        Assert.That(_quotaGovernor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, remainingTo8000, out var lease3), Is.True);
        lease3.Commit();

        // At 8,000: all outgoing (2..4) are blocked, while CriticalReadAndMonitor (1) can still reserve up to 10,000
        var blockedCmdResult = _youtubeMessenger.Send("понг", category: ChatSendCategory.CommandReply);
        var readCanAfford = _quotaGovernor.CanAfford(YouTubeQuotaPriority.CriticalReadAndMonitor, 5);

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.SentMessagesCount, Is.Zero);
            Assert.That(farewellResult, Is.EqualTo(ChatEnqueueResult.QuotaBudgetReached));
            Assert.That(blockedCmdResult, Is.EqualTo(ChatEnqueueResult.QuotaBudgetReached));
            Assert.That(readCanAfford, Is.True);
            Assert.That(_sentToYouTube.Count, Is.EqualTo(3));
            Assert.That(_sentToYouTube.Select(x => x.Text), Is.EqualTo(new[]
            {
                "Бот вошёл в чат",
                "Привет, User1! понг",
                "Привет, User3! понг",
            }));
        });
    }

    private Task PublishYouTubeViewerMessageAsync(
        string messageId,
        string userId,
        string displayName,
        string text,
        bool isModerator = false)
    {
        var msg = new ChatMessage(
            StreamingPlatform.YouTube,
            ChannelId,
            messageId,
            userId,
            displayName,
            displayName,
            text,
            [],
            [],
            false,
            isModerator,
            false,
            false,
            false);

        return _eventBus.PublishAsync(new RawChatMessageReceived(msg, _clock.GetUtcNow()), CancellationToken.None);
    }

    private async Task WaitForSentCountAsync(int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (_sentToYouTube.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
    }

    private sealed class StubAuthService : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>("token-ok");

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => true;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }

    private sealed class TestTextCommand(string canonical, string replyText) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
            => Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(replyText));
    }

    private sealed class StubSendClient(
        Func<string, string, YouTubeQuotaPriority, CancellationToken, Task<YouTubeInsertMessageResult>> handler)
        : IYouTubeLiveChatSendClient
    {
        public Task<YouTubeInsertMessageResult> InsertMessageAsync(
            string liveChatId,
            string messageText,
            YouTubeQuotaPriority priority,
            CancellationToken cancellationToken)
        {
            return handler(liveChatId, messageText, priority, cancellationToken);
        }
    }
}
