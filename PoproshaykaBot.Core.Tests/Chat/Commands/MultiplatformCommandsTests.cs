using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Tests.Polls;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public sealed class MultiplatformCommandsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-mp-cmds-" + Guid.NewGuid().ToString("N"));
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
    public async Task HelpCommand_HidesMetadataCommands_OnYouTube_And_ShowsToViewerOnTwitch()
    {
        var twitch = new FakeChatPlatform(
            StreamingPlatform.Twitch,
            "Twitch",
            PlatformCapabilities.Reply | PlatformCapabilities.ChannelMetadataEdit);
        var youtube = new FakeChatPlatform(
            StreamingPlatform.YouTube,
            "YouTube",
            PlatformCapabilities.ServerChatHistory);
        var registry = new ChatPlatformRegistry([twitch, youtube]);

        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        var applier = Substitute.For<IChannelInformationApplier>();
        var resolver = Substitute.For<IGameCategoryResolver>();

        var title = new TitleCommand(applier, NullLogger<TitleCommand>.Instance);
        var game = new GameCommand(applier, resolver, NullLogger<GameCommand>.Instance);
        var profile = new ProfileCommand(null!, NullLogger<ProfileCommand>.Instance);
        var hello = new SimpleCommand("привет", "поздороваться");

        Assert.That(profile.RequiredCapabilities, Is.EqualTo(PlatformCapabilities.ChannelMetadataEdit));

        var time = new TestTimeProvider { UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) };
        var processor = new ChatCommandProcessor(
            [hello, title, game, profile],
            commandSettingsStore,
            new(time),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: registry);
        processor.Register(new HelpCommand(processor.GetAvailableCommands, processor.Prefix));

        var twitchViewerContext = CreateContext(StreamingPlatform.Twitch, isMod: false);
        var ytModContext = CreateContext(StreamingPlatform.YouTube, isMod: true);

        var twitchHelp = await processor.TryProcessAsync("!помощь", twitchViewerContext, CancellationToken.None);
        var ytHelp = await processor.TryProcessAsync("!помощь", ytModContext, CancellationToken.None);
        var ytTitleHelp = await processor.TryProcessAsync("!помощь title", ytModContext, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(twitchHelp.Response!.Text, Does.Contain("!title"));
            Assert.That(twitchHelp.Response.Text, Does.Contain("!game"));
            Assert.That(twitchHelp.Response.Text, Does.Contain("!пресет"));
            Assert.That(twitchHelp.Response.Text, Does.Contain("!привет"));

            Assert.That(ytHelp.Response!.Text, Does.Contain("!привет"));
            Assert.That(ytHelp.Response.Text, Does.Not.Contain("!title"));
            Assert.That(ytHelp.Response.Text, Does.Not.Contain("!game"));
            Assert.That(ytHelp.Response.Text, Does.Not.Contain("!пресет"));

            Assert.That(ytTitleHelp.Response!.Text, Does.Not.Contain("обновить название"));
            Assert.That(ytTitleHelp.Response.Text, Does.StartWith("📋 Команды:"));
        });
    }

    [Test]
    public async Task MetadataCommands_AreRejectedSilently_WithoutUnknownLog_WhenCapabilityMissing()
    {
        var twitch = new FakeChatPlatform(
            StreamingPlatform.Twitch,
            "Twitch",
            PlatformCapabilities.Reply | PlatformCapabilities.ChannelMetadataEdit);
        var youtube = new FakeChatPlatform(
            StreamingPlatform.YouTube,
            "YouTube",
            PlatformCapabilities.ServerChatHistory);
        var registry = new ChatPlatformRegistry([twitch, youtube]);

        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        var applier = Substitute.For<IChannelInformationApplier>();
        var resolver = Substitute.For<IGameCategoryResolver>();

        var title = new TitleCommand(applier, NullLogger<TitleCommand>.Instance);
        var game = new GameCommand(applier, resolver, NullLogger<GameCommand>.Instance);

        var time = new TestTimeProvider { UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) };
        var processor = new ChatCommandProcessor(
            [title, game],
            commandSettingsStore,
            new(time),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: registry);

        var ytTitle = await processor.TryProcessAsync("!title Новое", CreateContext(StreamingPlatform.YouTube, isMod: true), CancellationToken.None);
        var ytGame = await processor.TryProcessAsync("!game Dota 2", CreateContext(StreamingPlatform.YouTube, isMod: true), CancellationToken.None);
        var twTitle = await processor.TryProcessAsync("!title Новое", CreateContext(StreamingPlatform.Twitch, isMod: true), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ytTitle.IsCommand, Is.False);
            Assert.That(ytGame.IsCommand, Is.False);
            Assert.That(twTitle.IsCommand, Is.True);
            Assert.That(twTitle.Response!.Text, Is.EqualTo("Название обновлено"));
        });

        await applier.Received(1).ApplyPatchAsync("Новое", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AudienceTracker_IsPartitionedByPlatform_AcrossPipelineActiveUsersAndBye()
    {
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.Reply);
        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None);
        var registry = new ChatPlatformRegistry([twitch, youtube]);
        var channels = new PlatformChannelRegistry();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var settings = new AppSettings();
        settings.Twitch.Messages.WelcomeEnabled = false;
        settings.Twitch.Messages.FarewellEnabled = true;
        settings.Twitch.Messages.Farewell = "Пока, {username}!";
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        settingsManager.Current.Returns(settings);

        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_root, "obs.json"));
        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        commandSettingsStore.Mutate(s => s.Commands["пользователи"] = new() { ResponseTarget = CommandResponseTarget.Chat });
        commandSettingsStore.Mutate(s => s.Commands["пока"] = new() { ResponseTarget = CommandResponseTarget.Chat });

        var audienceTracker = new AudienceTracker(settingsManager);
        var time = new TestTimeProvider { UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) };
        var processor = new ChatCommandProcessor(
            [new ActiveUsersCommand(audienceTracker), new ByeCommand(audienceTracker)],
            commandSettingsStore,
            new(time),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: registry);

        using var handler = new ChatPipelineHandler(
            registry,
            channels,
            settingsManager,
            obsChatStore,
            commandSettingsStore,
            audienceTracker,
            processor,
            new(time),
            bus,
            NullLogger<ChatPipelineHandler>.Instance);

        await handler.HandleAsync(CreateRaw(StreamingPlatform.Twitch, "tw-u1", "TwitchUser", "привет"), CancellationToken.None);
        await handler.HandleAsync(CreateRaw(StreamingPlatform.YouTube, "yt-u2", "YouTubeUser", "всем привет"), CancellationToken.None);

        await handler.HandleAsync(CreateRaw(StreamingPlatform.Twitch, "tw-caller", "CallerTW", "!пользователи"), CancellationToken.None);
        await handler.HandleAsync(CreateRaw(StreamingPlatform.YouTube, "yt-caller", "CallerYT", "!пользователи"), CancellationToken.None);

        await handler.HandleAsync(CreateRaw(StreamingPlatform.YouTube, "yt-u2", "YouTubeUser", "!пока"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(twitch.FakeMessenger.Sent[^1].Text, Does.Contain("TwitchUser").And.Not.Contain("YouTubeUser"));
            Assert.That(youtube.FakeMessenger.Sent[0].Text, Does.Contain("YouTubeUser").And.Not.Contain("TwitchUser"));
            Assert.That(youtube.FakeMessenger.Sent[1].Text, Is.EqualTo("Пока, YouTubeUser!"));
            Assert.That(audienceTracker.ForPlatform(StreamingPlatform.Twitch).ActiveUserCount, Is.EqualTo(2));
            Assert.That(audienceTracker.ForPlatform(StreamingPlatform.YouTube).ActiveUserCount, Is.EqualTo(1));
        });
    }

    private static CommandContext CreateContext(StreamingPlatform platform, bool isMod)
    {
        return new()
        {
            Platform = platform,
            Channel = "chan",
            MessageId = "m1",
            UserId = "u1",
            Username = "user",
            DisplayName = "User",
            IsBroadcaster = false,
            IsModerator = isMod,
        };
    }

    private static RawChatMessageReceived CreateRaw(StreamingPlatform platform, string userId, string displayName, string text)
    {
        return new(new(
                platform,
                "chan",
                Guid.NewGuid().ToString("N"),
                userId,
                displayName.ToLowerInvariant(),
                displayName,
                text,
                [],
                [],
                false,
                false,
                false,
                false,
                false),
            DateTimeOffset.UtcNow);
    }

    private sealed class SimpleCommand(string canonical, string description) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => description;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal("ok"));
    }
}
