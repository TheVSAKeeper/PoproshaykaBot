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
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatPipelineOrderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-pipeline-order-" + Guid.NewGuid().ToString("N"));
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task Inbound_ChatMessageReceived_Publishes_Before_Command_Response_And_Closed_Origin_Cannot_Reply(bool switchWhileRunning)
    {
        var time = new TestTimeProvider
        {
            UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
        };
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var collector = new OrderedEventCollector();
        using var sub = bus.Subscribe(collector);

        var helix = Substitute.For<ITwitchHelixClient>();
        var broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        var botUserIdProvider = Substitute.For<IBotUserIdProvider>();
        broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("1");
        botUserIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("2");

        var tracker = new CommandResponseTracker(time);
        var sender = new ChatSender(helix,
            broadcasterIdProvider,
            botUserIdProvider,
            new FakeTargetChannelProvider(),
            tracker,
            NullLogger<ChatSender>.Instance);

        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        commandSettingsStore.Mutate(s => s.Commands["ранг"] = new()
        {
            ResponseTarget = CommandResponseTarget.Overlay,
        });

        var settings = new AppSettings();
        settings.Twitch.Messages.WelcomeEnabled = false;
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_root, "obs-chat.json"));
        var accountsStore = new AccountsStore(null, Path.Combine(_root, "accounts.json"));
        var messenger = new TwitchChatMessenger(sender, accountsStore, NullLogger<TwitchChatMessenger>.Instance);
        var channels = new PlatformChannelRegistry();
        var twitchPlatform = new TwitchChatPlatform(Substitute.For<IStreamStatus>(),
            messenger,
            new ChatDecorationsProvider(helix),
            channels,
            accountsStore);
        var platformRegistry = new ChatPlatformRegistry([twitchPlatform]);
        var command = new BlockingCommand();
        var processor = new ChatCommandProcessor([command],
            commandSettingsStore,
            new(time),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: platformRegistry);

        using var handler = new ChatPipelineHandler(platformRegistry,
            channels,
            settingsManager,
            obsChatStore,
            commandSettingsStore,
            new AudienceTracker(settingsManager),
            processor,
            tracker,
            bus,
            NullLogger<ChatPipelineHandler>.Instance);

        var origin = new ChatOrigin(new(StreamingPlatform.Twitch, "1"), "1");
        var raw = new RawChatMessageReceived(new(StreamingPlatform.Twitch,
                "test-channel",
                "incoming-1",
                "u-1",
                "viewer",
                "Зритель",
                "!ранг",
                [],
                [],
                false,
                false,
                false,
                false,
                false,
                ChannelId: "1",
                Origin: origin),
            DateTimeOffset.UtcNow);

        var handleTask = handler.HandleAsync(raw, CancellationToken.None);

        await command.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await collector.FirstUserMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(collector.Snapshot(), Has.Count.EqualTo(1), "Пока команда выполняется, опубликован только входящий ChatMessageReceived");
        Assert.That(collector.Snapshot()[0].IsBot, Is.False);
        Assert.That(collector.Snapshot()[0].ChannelKey, Is.EqualTo(origin.Channel));

        if (switchWhileRunning)
        {
            origin.Close();
        }

        command.Release.SetResult();
        await handleTask.WaitAsync(TimeSpan.FromSeconds(5));

        if (switchWhileRunning)
        {
            await handler.HandleAsync(raw with { Message = raw.Message with { Id = "late" } }, CancellationToken.None);
            Assert.That(collector.Snapshot(), Has.Count.EqualTo(1));
            return;
        }

        await collector.FirstBotMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = collector.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Has.Count.EqualTo(2));
            Assert.That(snapshot[0].IsBot, Is.False);
            Assert.That(snapshot[0].Text, Is.EqualTo("!ранг"));
            Assert.That(snapshot[1].IsBot, Is.True);
            Assert.That(snapshot[1].Origin, Is.SameAs(origin));
            Assert.That(snapshot[1].Text, Is.EqualTo("ответ команды"));
            Assert.That(snapshot[1].CommandResponse, Is.EqualTo(new CommandResponseMark("ранг", CommandResponseTarget.Overlay)));
        });
    }

    [Test]
    public void Audience_Follows_Channel_And_Ignores_The_Closed_Origin()
    {
        var settings = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        var audience = new AudienceTracker(settings);
        var first = new ChatOrigin(new(StreamingPlatform.Twitch, "1"), "1");
        var next = new ChatOrigin(new(StreamingPlatform.Twitch, "2"), "2");
        var youtube = new ChatOrigin(new(StreamingPlatform.YouTube, "1"), "chat");

        audience.ForIncomingMessage(first.Channel.Platform, first.Channel, first)!.OnUserMessage("viewer-1", "First");
        audience.ForIncomingMessage(youtube.Channel.Platform, youtube.Channel, youtube)!.OnUserMessage("viewer-1", "YouTube");
        first.Close();
        audience.ForIncomingMessage(next.Channel.Platform, next.Channel, next)!.OnUserMessage("viewer-2", "Next");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(audience.ForIncomingMessage(first.Channel.Platform, first.Channel, first), Is.Null);
            Assert.That(audience.ForPlatform(StreamingPlatform.Twitch, first.Channel).ActiveUserCount, Is.Zero);
            Assert.That(audience.ForPlatform(StreamingPlatform.Twitch, next.Channel).ActiveUserCount, Is.EqualTo(1));
            Assert.That(audience.ForPlatform(StreamingPlatform.YouTube, youtube.Channel).ActiveUserCount, Is.EqualTo(1));
        }
    }

    private sealed class BlockingCommand : IChatCommand
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Canonical => "ранг";

        public IReadOnlyCollection<string> Aliases => [];

        public string Description => "blocking test";

        public bool CanExecute(CommandContext context) => true;

        public async Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return OutgoingMessage.Normal("ответ команды");
        }
    }

    private sealed class OrderedEventCollector : IEventHandler<ChatMessageReceived>
    {
        private readonly List<ChatMessageReceived> _events = [];

        public TaskCompletionSource FirstUserMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstBotMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
        {
            lock (_events)
            {
                _events.Add(@event);
            }

            if (@event.IsBot)
            {
                FirstBotMessage.TrySetResult();
            }
            else
            {
                FirstUserMessage.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public List<ChatMessageReceived> Snapshot()
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }
}
