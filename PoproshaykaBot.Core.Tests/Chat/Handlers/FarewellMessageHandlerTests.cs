using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Tests.Chat.Handlers;

[TestFixture]
public sealed class FarewellMessageHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _twitchPlatform = new(StreamingPlatform.Twitch, "Twitch", isConnected: true);
        _platformRegistry = new([_twitchPlatform]);

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settings = new();
        _settingsManager.Current.Returns(_settings);

        _audienceTracker = new(_settingsManager);

        _messenger = Substitute.For<IBroadcastMessenger>();
        _eventBus = Substitute.For<IEventBus>();
        _eventBus.Subscribe(Arg.Any<IEventHandler<BotLifecyclePhaseChanged>>()).Returns(Substitute.For<IDisposable>());

        _handler = new(_platformRegistry,
            _audienceTracker,
            _messenger,
            _settingsManager,
            _eventBus,
            NullLogger<FarewellMessageHandler>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    private FakeChatPlatform _twitchPlatform = null!;
    private ChatPlatformRegistry _platformRegistry = null!;
    private SettingsManager _settingsManager = null!;
    private AudienceTracker _audienceTracker = null!;
    private IBroadcastMessenger _messenger = null!;
    private IEventBus _eventBus = null!;
    private AppSettings _settings = null!;
    private FarewellMessageHandler _handler = null!;

    [Test]
    public async Task DoesNothingForNonDisconnectingPhase()
    {
        _settings.Twitch.Messages.FarewellEnabled = true;
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u1", "Alice");

        await _handler.HandleAsync(new(BotLifecyclePhase.Connected), CancellationToken.None);
        await _handler.HandleAsync(new(BotLifecyclePhase.Connecting), CancellationToken.None);
        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnected), CancellationToken.None);

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), Arg.Any<string>());
    }

    [Test]
    public async Task SkipsAllMessagesWhenChannelIsUnknown()
    {
        _twitchPlatform.IsConnected = false;
        _settings.Twitch.Messages.FarewellEnabled = true;
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u1", "Alice");

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), Arg.Any<string>());
    }

    [Test]
    public async Task SendsCollectiveFarewellAndDisconnectionJoined()
    {
        _settings.Twitch.Messages.FarewellEnabled = true;
        _settings.Twitch.Messages.Farewell = "До свидания, {username}!";
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _settings.Twitch.Messages.Disconnection = "Пока-пока!";
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u1", "Alice");
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u2", "Bob");

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "До свидания, Alice, Bob! Пока-пока!");
    }

    [Test]
    public async Task SendsOnlyDisconnectionWhenFarewellIsDisabled()
    {
        _settings.Twitch.Messages.FarewellEnabled = false;
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _settings.Twitch.Messages.Disconnection = "Пока-пока!";
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u1", "Alice");

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "Пока-пока!");
    }

    [Test]
    public async Task SendsOnlyDisconnectionWhenAudienceTrackerIsEmpty()
    {
        _settings.Twitch.Messages.FarewellEnabled = true;
        _settings.Twitch.Messages.Farewell = "До свидания, {username}!";
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _settings.Twitch.Messages.Disconnection = "Пока-пока!";

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "Пока-пока!");
    }

    [Test]
    public async Task SendsOnlyCollectiveFarewellWhenDisconnectionDisabled()
    {
        _settings.Twitch.Messages.FarewellEnabled = true;
        _settings.Twitch.Messages.Farewell = "Спасибо, {username}!";
        _settings.Twitch.Messages.DisconnectionEnabled = false;
        _audienceTracker.ForPlatform(StreamingPlatform.Twitch).OnUserMessage("u1", "Alice");

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "Спасибо, Alice!");
    }

    [Test]
    public async Task SendsNothingWhenBothMessagesAreDisabledAndTrackerEmpty()
    {
        _settings.Twitch.Messages.FarewellEnabled = false;
        _settings.Twitch.Messages.DisconnectionEnabled = false;

        await _handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), Arg.Any<string>());
    }

    [TestCase(ChatEnqueueResult.Queued, LogLevel.Information, "поставлено в очередь")]
    [TestCase(ChatEnqueueResult.ObserverMode, LogLevel.Debug, "не поставлено в очередь ChatSender: режим наблюдателя")]
    [TestCase(ChatEnqueueResult.QueueClosed, LogLevel.Debug, "не поставлено в очередь ChatSender: очередь отправки закрыта")]
    public async Task LogsFarewellByEnqueueOutcome(ChatEnqueueResult outcome, LogLevel expectedLevel, string expectedText)
    {
        _settings.Twitch.Messages.DisconnectionEnabled = true;
        _settings.Twitch.Messages.Disconnection = "Пока-пока!";
        _messenger.Send(StreamingPlatform.Twitch, Arg.Any<string>()).Returns(outcome);

        var logger = new RecordingLogger<FarewellMessageHandler>();
        using var handler = new FarewellMessageHandler(_platformRegistry,
            _audienceTracker,
            _messenger,
            _settingsManager,
            _eventBus,
            logger);

        await handler.HandleAsync(new(BotLifecyclePhase.Disconnecting), CancellationToken.None);

        var farewellLines = logger.Entries.Where(e => e.Message.Contains("Прощальное сообщение")).ToList();

        Assert.That(farewellLines, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(farewellLines[0].Level, Is.EqualTo(expectedLevel));
            Assert.That(farewellLines[0].Message, Does.Contain(expectedText));
        });
    }
}
