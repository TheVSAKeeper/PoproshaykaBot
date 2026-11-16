using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public sealed class StreamStatusBroadcastHandlerTests
{
    private const string Channel = "test_channel";

    private IBroadcastMessenger _messenger = null!;
    private IBroadcastScheduler _scheduler = null!;
    private IBroadcastState _broadcastState = null!;
    private IBroadcastPlatformTargetsProvider _targetsProvider = null!;
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private InMemoryEventBus _eventBus = null!;
    private FakeTargetChannelProvider _targetChannel = null!;
    private StreamStatusBroadcastHandler _handler = null!;
    private bool _schedulerActive;
    private HashSet<StreamingPlatform> _livePlatforms = [];
    private HashSet<StreamingPlatform> _targetPlatforms = [];

    [SetUp]
    public void SetUp()
    {
        _messenger = Substitute.For<IBroadcastMessenger>();
        _scheduler = Substitute.For<IBroadcastScheduler>();
        _schedulerActive = false;
        _scheduler.IsActive.Returns(_ => _schedulerActive);
        _scheduler.When(s => s.Start(Arg.Any<string>())).Do(_ => _schedulerActive = true);
        _scheduler.When(s => s.Stop()).Do(_ => _schedulerActive = false);

        _livePlatforms = [];
        _broadcastState = Substitute.For<IBroadcastState>();
        _broadcastState.IsAnyLive.Returns(_ => _livePlatforms.Count > 0);
        _broadcastState.LivePlatforms.Returns(_ => _livePlatforms);
        _broadcastState.IsPlatformLive(Arg.Any<StreamingPlatform>())
            .Returns(callInfo => _livePlatforms.Contains(callInfo.Arg<StreamingPlatform>()));

        _targetPlatforms = [StreamingPlatform.Twitch, StreamingPlatform.YouTube];
        _targetsProvider = Substitute.For<IBroadcastPlatformTargetsProvider>();
        _targetsProvider.GetTargetPlatforms().Returns(_ => _targetPlatforms);

        _settings = new()
        {
            Twitch =
            {
                Channel = Channel,
                AutoBroadcast =
                {
                    AutoBroadcastEnabled = true,
                    BroadcastIntervalMinutes = 15,
                    StreamStatusNotificationsEnabled = true,
                    StreamStartMessage = "Стрим начался",
                    StreamStopMessage = "Стрим закончился",
                },
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settingsManager.Current.Returns(_settings);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _targetChannel = new(Channel);

        _handler = new(
            _scheduler,
            _messenger,
            _broadcastState,
            _targetsProvider,
            _targetChannel,
            _settingsManager,
            _eventBus,
            NullLogger<StreamStatusBroadcastHandler>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task BroadcastStarted_BotDisconnected_DoesNotStartScheduler()
    {
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));

        _scheduler.DidNotReceive().Start(Arg.Any<string>());
        _messenger.DidNotReceive().SendToConnected(Arg.Any<string>(), Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BroadcastStarted_AfterBotConnected_StartsSchedulerAndSendsStartMessage()
    {
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));

        _scheduler.Received(1).Start(Channel);
        _messenger.Received(1).SendToConnected("Стрим начался", ChatSendCategory.EventNotification);
    }

    [Test]
    public async Task BotConnects_WhileBroadcastIsLive_StartsSchedulerOnConnect()
    {
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _scheduler.Received(1).Start(Channel);
    }

    [Test]
    public async Task BotConnects_WhileBroadcastIsOffline_DoesNotStartScheduler()
    {
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _scheduler.DidNotReceive().Start(Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastEnded_BotDisconnected_DoesNotSendStopMessage()
    {
        await _eventBus.PublishAsync(new BroadcastEnded(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, DateTimeOffset.UtcNow));

        _messenger.DidNotReceive().SendToConnected(Arg.Any<string>(), Arg.Any<ChatSendCategory>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BroadcastEnded_AfterBotConnected_StopsSchedulerAndNotifiesOnlyLiveTransition(bool isCatchUp)
    {
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));
        _livePlatforms.Clear();

        await _eventBus.PublishAsync(new BroadcastEnded(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, DateTimeOffset.UtcNow, IsCatchUp: isCatchUp));

        _scheduler.Received(1).Stop();
        _messenger.Received(isCatchUp ? 0 : 1).SendToConnected("Стрим закончился", ChatSendCategory.EventNotification);
    }

    [Test]
    public async Task BroadcastStarted_WithIsCatchUpTrue_StartsSchedulerButDoesNotSendStartMessage()
    {
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow, IsCatchUp: true));

        _scheduler.Received(1).Start(Channel);
        _messenger.DidNotReceive().SendToConnected("Стрим начался", Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BotConnects_WhileBroadcastIsOnlineWithoutPending_DoesNotSendStartMessage()
    {
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());
        _messenger.DidNotReceive().SendToConnected("Стрим начался", Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BroadcastStarted_BotDisconnected_SendsStartMessageOnceOnTargetConnect()
    {
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));
        _messenger.DidNotReceive().SendToConnected("Стрим начался", Arg.Any<ChatSendCategory>());
        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());

        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "Стрим начался", ChatSendCategory.EventNotification);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnected));
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.Received(1).Send(StreamingPlatform.Twitch, "Стрим начался", ChatSendCategory.EventNotification);
    }

    [Test]
    public async Task BroadcastStarted_BroadcastOfflineOnConnect_DropsPendingStartNotification()
    {
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());

        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnected));
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BroadcastStarted_BotDisconnectedWithCatchUp_DoesNotSendStartMessageOnConnect()
    {
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow, IsCatchUp: true));

        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BroadcastEnded_BotDisconnected_DropsPendingStartNotification()
    {
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));
        await _eventBus.PublishAsync(new BroadcastEnded(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, DateTimeOffset.UtcNow));

        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

        _messenger.DidNotReceive().Send(Arg.Any<StreamingPlatform>(), "Стрим начался", Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task BroadcastStarted_ConnectedPlatformOutsideTargets_NotifiesWithoutStartingScheduler()
    {
        _targetPlatforms = [StreamingPlatform.YouTube];
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        _livePlatforms.Add(StreamingPlatform.Twitch);

        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));

        _scheduler.DidNotReceive().Start(Arg.Any<string>());
        _messenger.Received(1).SendToConnected("Стрим начался", ChatSendCategory.EventNotification);

        _livePlatforms.Add(StreamingPlatform.YouTube);
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Connected));

        _scheduler.Received(1).Start(Channel);
        _messenger.Received(1).Send(StreamingPlatform.YouTube, "Стрим начался", ChatSendCategory.EventNotification);
    }

    [Test]
    public async Task PlatformDisconnects_WhenLastConnectedTargetLeaves_StopsScheduler()
    {
        _targetPlatforms = [StreamingPlatform.Twitch, StreamingPlatform.YouTube];
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Connected));
        _livePlatforms.Add(StreamingPlatform.Twitch);
        await _eventBus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, Channel, null, DateTimeOffset.UtcNow));

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnected));
        _scheduler.DidNotReceive().Stop();

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Disconnected));
        _scheduler.Received(1).Stop();
    }
}
