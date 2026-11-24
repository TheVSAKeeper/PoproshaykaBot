using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class BotLifecycleAutomationHandlerTests
{
    private const string Channel = "streamer";

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("lifecycle-handler");
        _order = [];

        _settings = new()
        {
            Twitch =
            {
                Channel = Channel,
                BotLifecycleAutomation =
                {
                    AutoConnectOnStreamOnline = true,
                    AutoDisconnectOnStreamOffline = true,
                },
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settingsManager.Current.Returns(_settings);
        _lifecycleStore = new(_settingsManager, filePath: Path.Combine(_directory.FullName, "platform-lifecycle.json"));

        _connectionController = Substitute.For<IBotConnectionController>();

        _connectionController.StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>()).Returns(_ =>
        {
            _order.Add("стоп бота");
            return Task.CompletedTask;
        });

        _connectionController.When(x => x.StartConnection(Arg.Any<StreamingPlatform>())).Do(_ => _order.Add("старт бота"));

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _targetChannelProvider = new(Channel);
        _aggregator = new(
            _eventBus,
            Substitute.For<IBroadcastDraftCoordinator>(),
            _targetChannelProvider,
            TimeProvider.System,
            NullLogger<BroadcastAggregator>.Instance);

        _handler = new(_connectionController,
            new BotLifecycleAutomationSettingsProvider(_lifecycleStore),
            _aggregator,
            _targetChannelProvider,
            _eventBus,
            NullLogger<BotLifecycleAutomationHandler>.Instance);

        _continuationsDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _eventBus.Subscribe<StreamWentOffline>(async (_, _) =>
        {
            await Task.Yield();
            _order.Add("итоговая статистика поставлена в очередь");

            _eventBus.ContinueAfterPublish(() =>
            {
                _order.Add("проба продолжения");
                _continuationsDone.SetResult();
                return Task.CompletedTask;
            });
        });

        _eventBus.Subscribe<StreamWentOnline>((_, _) =>
        {
            _order.Add("поздний подписчик онлайна");
            return Task.CompletedTask;
        });
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
        _aggregator.Dispose();
        _directory.Delete(recursive: true);
    }

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private DirectoryInfo _directory = null!;
    private List<string> _order = null!;
    private TaskCompletionSource _continuationsDone = null!;
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private PlatformLifecycleSettingsStore _lifecycleStore = null!;
    private IBotConnectionController _connectionController = null!;
    private InMemoryEventBus _eventBus = null!;
    private BotLifecycleAutomationHandler _handler = null!;

    [Test]
    public async Task StreamWentOffline_StopsBotAfterEveryOtherSubscriberOfThePublication()
    {
        await SetPhaseAsync(BotLifecyclePhase.Connected);

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));
        await _continuationsDone.Task.WaitAsync(WaitTimeout);

        Assert.That(_order, Is.EqualTo(new[] { "итоговая статистика поставлена в очередь", "стоп бота", "проба продолжения" }),
            "Авто-стоп обгонит итоговую статистику, если запускать его прямо в обработчике.");

        await _connectionController.Received(1).StopAsync(StreamingPlatform.Twitch, BotStopMode.Graceful);
    }

    [Test]
    public async Task StreamWentOffline_PublishDoesNotWaitForTheStop()
    {
        var stopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _connectionController.StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>()).Returns(async _ =>
        {
            stopStarted.SetResult();
            await releaseStop.Task;
        });

        await SetPhaseAsync(BotLifecyclePhase.Connected);

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel)).WaitAsync(WaitTimeout);
        await stopStarted.Task.WaitAsync(WaitTimeout);

        Assert.That(_continuationsDone.Task.IsCompleted, Is.False,
            "Публикация вернулась, а остановка ещё идёт – следующие продолжения ждут её.");

        releaseStop.SetResult();
        await _continuationsDone.Task.WaitAsync(WaitTimeout);
    }

    [Test]
    public async Task StreamWentOffline_StopFailure_DoesNotBreakPublicationOrTheNextContinuation()
    {
        await SetPhaseAsync(BotLifecyclePhase.Connected);

        _connectionController.StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>())
            .Returns(_ => Task.FromException(new InvalidOperationException("boom")));

        Assert.DoesNotThrowAsync(async () => await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel)));

        await _continuationsDone.Task.WaitAsync(WaitTimeout);

        Assert.That(_order, Is.EqualTo(new[] { "итоговая статистика поставлена в очередь", "проба продолжения" }));
    }

    [Test]
    public async Task StreamWentOffline_StreamBackOnlineBeforeTheContinuation_DoesNotStopBot()
    {
        await SetPhaseAsync(BotLifecyclePhase.Connected);

        using var backOnline = _eventBus.Subscribe<StreamWentOffline>((_, cancellationToken) =>
            _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, null), cancellationToken));

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));
        await _continuationsDone.Task.WaitAsync(WaitTimeout);

        await _connectionController.DidNotReceive().StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>());
        _connectionController.DidNotReceive().StartConnection(Arg.Any<StreamingPlatform>());

        Assert.That(_order, Is.EqualTo(new[]
        {
            "итоговая статистика поставлена в очередь",
            "поздний подписчик онлайна",
            "проба продолжения",
        }));
    }

    [TestCase(false, BotLifecyclePhase.Connected, false, TestName = "Авто-отключение выключено настройкой")]
    [TestCase(true, BotLifecyclePhase.Connected, true, TestName = "Офлайн-снимок при подключении (catch-up)")]
    [TestCase(true, BotLifecyclePhase.Idle, false, TestName = "Бот ещё не подключался")]
    [TestCase(true, BotLifecyclePhase.Disconnected, false, TestName = "Бот уже отключён")]
    [TestCase(true, BotLifecyclePhase.Connecting, false, TestName = "Бот в процессе подключения")]
    public async Task StreamWentOffline_DoesNotStopBot(bool autoDisconnect, BotLifecyclePhase phase, bool isCatchUp)
    {
        _settings.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline = autoDisconnect;
        await SetPhaseAsync(phase);

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, isCatchUp));
        await _continuationsDone.Task.WaitAsync(WaitTimeout);

        await _connectionController.DidNotReceive().StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>());
        Assert.That(_order, Is.EqualTo(new[] { "итоговая статистика поставлена в очередь", "проба продолжения" }));
    }

    [TestCase(BotLifecyclePhase.Idle)]
    [TestCase(BotLifecyclePhase.Disconnected)]
    [TestCase(BotLifecyclePhase.Cancelled)]
    [TestCase(BotLifecyclePhase.Failed)]
    public async Task StreamWentOnline_StartsConnectionFromDisconnectedPhase(BotLifecyclePhase phase)
    {
        await SetPhaseAsync(phase);

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, null));

        _connectionController.Received(1).StartConnection(StreamingPlatform.Twitch);
    }

    [TestCase(false, BotLifecyclePhase.Idle, false, TestName = "Авто-подключение выключено настройкой")]
    [TestCase(true, BotLifecyclePhase.Connected, false, TestName = "Бот уже подключён")]
    [TestCase(true, BotLifecyclePhase.Idle, true, TestName = "Менеджер подключений занят")]
    public async Task StreamWentOnline_DoesNotStartConnection(bool autoConnect, BotLifecyclePhase phase, bool isBusy)
    {
        _settings.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = autoConnect;
        _connectionController.IsPlatformBusy(StreamingPlatform.Twitch).Returns(isBusy);
        await SetPhaseAsync(phase);

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, null));

        _connectionController.DidNotReceive().StartConnection(Arg.Any<StreamingPlatform>());
    }

    [Test]
    public async Task StreamWentOnlineAndOffline_DebugChannel_DoesNotConnectOrDisconnect()
    {
        _targetChannelProvider.OwnChannel = "own-channel";
        _targetChannelProvider.IsDebugSession = true;
        _targetChannelProvider.Login = "debug-chan";
        await SetPhaseAsync(BotLifecyclePhase.Idle);
        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, null));

        await SetPhaseAsync(BotLifecyclePhase.Connected);
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));

        _connectionController.DidNotReceive().StartConnection(Arg.Any<StreamingPlatform>());
        await _connectionController.DidNotReceive().StopAsync(Arg.Any<StreamingPlatform>(), Arg.Any<BotStopMode>());
    }

    [Test]
    public async Task StreamWentOnlineAndOffline_YouTubeEnabled_ConnectsAndDisconnectsYouTube()
    {
        _lifecycleStore.Set(StreamingPlatform.YouTube, new()
        {
            AutoConnectOnStreamOnline = true,
            AutoDisconnectOnStreamOffline = true,
        });

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Idle));
        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "UC_MAIN", null));

        _connectionController.Received(1).StartConnection(StreamingPlatform.YouTube);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Connected));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "UC_MAIN"));
        await _continuationsDone.Task.WaitAsync(WaitTimeout);

        await _connectionController.Received(1).StopAsync(StreamingPlatform.YouTube, BotStopMode.Graceful);
    }

    [Test]
    public async Task ActiveChannelChanged_ToOnlineChannel_AutoConnectsWhenDisconnected()
    {
        var monitor = Substitute.For<IPlatformChannelMonitor>();
        monitor.Platform.Returns(StreamingPlatform.Twitch);
        var newChannel = new ChannelKey(StreamingPlatform.Twitch, "177128531");
        monitor.GetSnapshot(newChannel).Returns(new ChannelMonitoringSnapshot(
            newChannel,
            "bobito217",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Online,
            null,
            DateTimeOffset.UtcNow,
            false));

        using var handlerWithMonitor = new BotLifecycleAutomationHandler(
            _connectionController,
            new BotLifecycleAutomationSettingsProvider(_lifecycleStore),
            _aggregator,
            _targetChannelProvider,
            _eventBus,
            NullLogger<BotLifecycleAutomationHandler>.Instance,
            [monitor]);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Idle));
        await _eventBus.PublishAsync(new ActiveChannelChanged(
            StreamingPlatform.Twitch,
            new ChannelKey(StreamingPlatform.Twitch, "107618204"),
            newChannel,
            "bobito217",
            ChannelSwitchMode.Manual));

        _connectionController.Received(1).StartConnection(StreamingPlatform.Twitch);
    }

    [Test]
    public async Task ActiveChannelChanged_ToOfflineChannel_DoesNotAutoConnect()
    {
        var monitor = Substitute.For<IPlatformChannelMonitor>();
        monitor.Platform.Returns(StreamingPlatform.Twitch);
        var newChannel = new ChannelKey(StreamingPlatform.Twitch, "177128531");
        monitor.GetSnapshot(newChannel).Returns(new ChannelMonitoringSnapshot(
            newChannel,
            "bobito217",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Offline,
            null,
            DateTimeOffset.UtcNow,
            false));

        using var handlerWithMonitor = new BotLifecycleAutomationHandler(
            _connectionController,
            new BotLifecycleAutomationSettingsProvider(_lifecycleStore),
            _aggregator,
            _targetChannelProvider,
            _eventBus,
            NullLogger<BotLifecycleAutomationHandler>.Instance,
            [monitor]);

        await _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Idle));
        await _eventBus.PublishAsync(new ActiveChannelChanged(
            StreamingPlatform.Twitch,
            new ChannelKey(StreamingPlatform.Twitch, "107618204"),
            newChannel,
            "bobito217",
            ChannelSwitchMode.Manual));

        _connectionController.DidNotReceive().StartConnection(Arg.Any<StreamingPlatform>());
    }

    private FakeTargetChannelProvider _targetChannelProvider = null!;
    private BroadcastAggregator _aggregator = null!;

    private Task SetPhaseAsync(BotLifecyclePhase phase)
    {
        return _eventBus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, phase));
    }
}
