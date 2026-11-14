using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class CommandResponseRoutingTests
{
    private const PlatformCapabilities TwitchCaps =
        PlatformCapabilities.Reply | PlatformCapabilities.Whisper | PlatformCapabilities.EmotePositions;

    private const PlatformCapabilities YouTubeCaps = PlatformCapabilities.ServerChatHistory;

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-routing-" + Guid.NewGuid().ToString("N"));
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

    [TestCase(CommandResponseTarget.None, DeliveryType.Normal, TwitchCaps, CommandDeliveryAction.None, null)]
    [TestCase(CommandResponseTarget.Overlay, DeliveryType.Reply, TwitchCaps, CommandDeliveryAction.OverlayOnly, null)]
    [TestCase(CommandResponseTarget.Overlay, DeliveryType.Reply, YouTubeCaps, CommandDeliveryAction.OverlayOnly, null)]
    [TestCase(CommandResponseTarget.Chat, DeliveryType.Normal, TwitchCaps, CommandDeliveryAction.Send, null)]
    [TestCase(CommandResponseTarget.Chat, DeliveryType.Reply, TwitchCaps, CommandDeliveryAction.ReplyThread, "msg-1")]
    [TestCase(CommandResponseTarget.Chat, DeliveryType.Normal, YouTubeCaps, CommandDeliveryAction.Send, null)]
    [TestCase(CommandResponseTarget.Chat, DeliveryType.Reply, YouTubeCaps, CommandDeliveryAction.Send, null)]
    [TestCase(CommandResponseTarget.Chat | CommandResponseTarget.Overlay, DeliveryType.Reply, YouTubeCaps, CommandDeliveryAction.Send, null)]
    [TestCase(CommandSettings.CallerOnly, DeliveryType.Normal, TwitchCaps, CommandDeliveryAction.ReplyThread, "msg-1")]
    [TestCase(CommandSettings.CallerOnly, DeliveryType.Normal, YouTubeCaps, CommandDeliveryAction.MentionSend, null)]
    [TestCase(CommandSettings.WhisperToCaller, DeliveryType.Normal, TwitchCaps, CommandDeliveryAction.Whisper, null)]
    [TestCase(CommandSettings.WhisperToCaller, DeliveryType.Normal, PlatformCapabilities.Reply, CommandDeliveryAction.ReplyThread, "msg-1")]
    [TestCase(CommandSettings.WhisperToCaller, DeliveryType.Normal, YouTubeCaps, CommandDeliveryAction.MentionSend, null)]
    public void Resolve_MatchesTable24(
        CommandResponseTarget target,
        DeliveryType delivery,
        PlatformCapabilities capabilities,
        CommandDeliveryAction expectedAction,
        string? expectedReplyTo)
    {
        var response = delivery == DeliveryType.Reply
            ? OutgoingMessage.Reply("ответ", "msg-1")
            : OutgoingMessage.Normal("ответ");

        var decision = CommandResponseRouting.Resolve(target, response, "msg-1", capabilities);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(expectedAction));
            Assert.That(decision.ReplyToMessageId, Is.EqualTo(expectedReplyTo));
        });
    }

    [TestCase("Мария", "привет", "@Мария привет")]
    [TestCase("@Мария", "привет", "@Мария привет")]
    [TestCase("  @@Мария ", "привет", "@Мария привет")]
    [TestCase("   ", "привет", "привет")]
    public void PrependCallerMention_NormalizesLeadingAt(string displayName, string text, string expected)
    {
        Assert.That(CommandResponseRouting.PrependCallerMention(displayName, text), Is.EqualTo(expected));
    }

    [Test]
    public async Task ChatPipelineHandler_OnYouTubeWithoutReplyOrWhisper_RoutesPerTable24()
    {
        var youtube = new FakeChatPlatform(
            StreamingPlatform.YouTube,
            "YouTube",
            PlatformCapabilities.ServerChatHistory,
            botIdentity: new("yt-bot", "BotYT"));
        var registry = new ChatPlatformRegistry([youtube]);
        var channels = new PlatformChannelRegistry();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var settings = new AppSettings();
        settings.Twitch.Messages.WelcomeEnabled = false;
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_root, "obs.json"));
        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));

        var time = new TestTimeProvider { UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) };
        var normalCmd = new StubCommand("чат", DeliveryType.Normal);
        var replyCmd = new StubCommand("реп", DeliveryType.Reply);
        var callerCmd = new StubCommand("мне", DeliveryType.Normal);
        var whisperCmd = new StubCommand("шепот", DeliveryType.Normal);

        commandSettingsStore.Mutate(s =>
        {
            s.Commands["чат"] = new() { ResponseTarget = CommandResponseTarget.Chat };
            s.Commands["реп"] = new() { ResponseTarget = CommandResponseTarget.Chat | CommandResponseTarget.Overlay };
            s.Commands["мне"] = new() { ResponseTarget = CommandSettings.CallerOnly };
            s.Commands["шепот"] = new() { ResponseTarget = CommandSettings.WhisperToCaller };
        });

        var processor = new ChatCommandProcessor(
            [normalCmd, replyCmd, callerCmd, whisperCmd],
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
            new AudienceTracker(settingsManager),
            processor,
            new(time),
            bus,
            NullLogger<ChatPipelineHandler>.Instance);

        await handler.HandleAsync(CreateYouTubeRaw("m1", "!чат"), CancellationToken.None);
        await handler.HandleAsync(CreateYouTubeRaw("m2", "!реп"), CancellationToken.None);
        await handler.HandleAsync(CreateYouTubeRaw("m3", "!мне"), CancellationToken.None);
        await handler.HandleAsync(CreateYouTubeRaw("m4", "!шепот"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(youtube.FakeMessenger.Sent.Select(x => x.Text), Is.EqualTo(new[]
            {
                "ответ чат",
                "ответ реп",
                "@Viewer ответ мне",
                "@Viewer ответ шепот",
            }));
            Assert.That(youtube.FakeMessenger.Replies, Is.Empty);
            Assert.That(youtube.FakeMessenger.Whispers, Is.Empty);
        });
    }

    private static RawChatMessageReceived CreateYouTubeRaw(string id, string text)
    {
        return new(new(
                StreamingPlatform.YouTube,
                "yt-live-1",
                id,
                "yt-u1",
                "viewer",
                "@Viewer",
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

    private sealed class StubCommand(string canonical, DeliveryType delivery) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public bool CanExecute(CommandContext context) => true;

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var msg = delivery == DeliveryType.Reply
                ? OutgoingMessage.Reply($"ответ {canonical}", context.MessageId)
                : OutgoingMessage.Normal($"ответ {canonical}");
            return Task.FromResult<OutgoingMessage?>(msg);
        }
    }
}
