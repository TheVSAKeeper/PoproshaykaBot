using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatHistoryPipelineIsolationTests
{
    private sealed class FakeServerHistoryPlatform : IChatPlatform
    {
        public StreamingPlatform Id => StreamingPlatform.YouTube;

        public string DisplayName => "YouTube";

        public PlatformCapabilities Capabilities => PlatformCapabilities.ServerChatHistory;

        public bool IsEnabled => true;

        public bool IsConnected => true;

        public IStreamStatus StreamStatus { get; } = Substitute.For<IStreamStatus>();

        public IPlatformMessenger Messenger { get; } = Substitute.For<IPlatformMessenger>();

        public IChatDecorations Decorations { get; } = Substitute.For<IChatDecorations>();
    }

    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-history-isolation");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public async Task ServerHistoryBatch_PublishesChatHistoryLoaded_WithoutTriggeringCommandsGreetingsOrStatistics()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));
        settingsManager.Mutate(s => s.Twitch.Messages.WelcomeEnabled = true);
        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_directory.FullName, "obs-chat.json"));
        var chatSettingsStore = new ChatSettingsStore(settingsManager, null, Path.Combine(_directory.FullName, "chat.json"));
        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_directory.FullName, "commands.json"));

        var fakePlatform = new FakeServerHistoryPlatform();
        fakePlatform.Decorations.ExtractEmotes(Arg.Any<ChatMessage>(), Arg.Any<int>()).Returns([]);
        fakePlatform.Decorations.ExtractBadgeUrls(Arg.Any<IReadOnlyList<(string SetId, string BadgeId)>>(), Arg.Any<int>())
            .Returns(new Dictionary<string, string>(StringComparer.Ordinal));

        var registry = new ChatPlatformRegistry([fakePlatform]);
        var audienceTracker = new AudienceTracker(settingsManager);
        var command = Substitute.For<IChatCommand>();
        command.Canonical.Returns("тест");
        command.Aliases.Returns(["!тест"]);

        var commandUsage = new CommandUsageRepository(TimeProvider.System);
        var processor = new ChatCommandProcessor([command], commandSettingsStore, commandUsage, NullLogger<ChatCommandProcessor>.Instance, platformRegistry: registry);
        using var pipeline = new ChatPipelineHandler(
            registry,
            new PlatformChannelRegistry(),
            settingsManager,
            obsChatStore,
            commandSettingsStore,
            audienceTracker,
            processor,
            new CommandResponseTracker(TimeProvider.System),
            bus,
            NullLogger<ChatPipelineHandler>.Instance);

        var userStats = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var botStats = Substitute.For<IBotStatisticsRepository>();
        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("chan", "chan", false, true));
        using var statsHandler = new StatisticsTrackingHandler(userStats, botStats, targetChannel, bus);

        using var historyManager = new ChatHistoryManager(settingsManager, bus, chatSettingsStore);
        using var batchHandler = new ChatHistoryBatchHandler(registry, obsChatStore, historyManager, bus);

        var loadedEvents = new List<ChatHistoryLoaded>();
        var receivedEvents = new List<ChatMessageReceived>();
        using var subLoaded = bus.Subscribe<ChatHistoryLoaded>(e => loadedEvents.Add(e));
        using var subReceived = bus.Subscribe<ChatMessageReceived>(e => receivedEvents.Add(e));

        var scope = ChatHistoryScope.ForYouTube("UC-1", "live-1", "Chan");
        var now = DateTimeOffset.UtcNow;

        var rawMessages = new List<(ChatMessage, DateTimeOffset)>
        {
            (new ChatMessage(StreamingPlatform.YouTube, "Chan", "hist-1", "u-1", "viewer1", "Viewer1", "!тест", [], [], false, false, false, false, ChannelId: "UC-1", ChatSessionId: "live-1"), now.AddMinutes(-3)),
            (new ChatMessage(StreamingPlatform.YouTube, "Chan", "hist-deleted", "u-2", "viewer2", "Viewer2", "удалят", [], [], false, false, false, false, ChannelId: "UC-1", ChatSessionId: "live-1"), now.AddMinutes(-2)),
        };

        var moderations = new List<ChatModerationAction>
        {
            new(ChatModerationActionKind.MessageDeleted, "hist-deleted", null, now.AddMinutes(-1)),
        };

        await bus.PublishAsync(new RawChatHistoryReceived(scope, rawMessages, moderations));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedEvents, Has.Count.EqualTo(1));
            Assert.That(loadedEvents[0].AddedEntries.Select(static m => m.MessageId), Is.EqualTo(new[] { "hist-1" }));
            Assert.That(loadedEvents[0].AddedEntries[0].IsHistorical, Is.True);
            Assert.That(receivedEvents, Is.Empty);
            await command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
            fakePlatform.Messenger.DidNotReceiveWithAnyArgs().Send(default!, default, default);
            fakePlatform.Messenger.DidNotReceiveWithAnyArgs().Reply(default!, default!, default!, default, default);
            fakePlatform.Messenger.DidNotReceiveWithAnyArgs().Whisper(default!, default!, default!, default!, default, default);
            Assert.That(userStats.ForPlatform(StreamingPlatform.YouTube).GetById("u-1"), Is.Null);
            Assert.That(audienceTracker.ForPlatform(StreamingPlatform.YouTube).ActiveUserCount, Is.Zero);
        }
    }
}
