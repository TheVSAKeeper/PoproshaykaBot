using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Diagnostics;

[TestFixture]
public sealed class DiagnosticsSnapshotSourceTests
{
    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _metrics = new();
        _memoryUsageSink = new();

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance, _metrics);
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(new AppSettings());

        var eventSubClient = Substitute.For<ITwitchEventSubClient>();
        var helix = Substitute.For<ITwitchHelixClient>();
        var broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        var botUserIdProvider = Substitute.For<IBotUserIdProvider>();

        _targetChannelProvider = Substitute.For<ITargetChannelProvider>();
        _targetChannelProvider.Current.Returns(new TargetChannelState(string.Empty, string.Empty, false, false));

        var botConnection = Substitute.For<IBotConnectionController>();
        botConnection.GetPhase(StreamingPlatform.Twitch).Returns(BotLifecyclePhase.Idle);

        _chatSender = new(helix,
            broadcasterIdProvider,
            botUserIdProvider,
            _targetChannelProvider,
            new(TimeProvider.System),
            NullLogger<ChatSender>.Instance);

        var chatIngestion = new ChatIngestionService(eventSubClient,
            helix,
            broadcasterIdProvider,
            botUserIdProvider,
            settingsManager,
            bus,
            TimeProvider.System,
            NullLogger<ChatIngestionService>.Instance);

        var obs = new ObsIntegrationService(Substitute.For<IObsWebSocketClient>(),
            settingsManager,
            NullLogger<ObsIntegrationService>.Instance);

        var obsChatStore = new ObsChatStore(bus,
            NullLogger<ObsChatStore>.Instance,
            Path.Combine(_directory, "obs-chat.json"));

        _sse = new(settingsManager,
            obsChatStore,
            NullLogger<SseService>.Instance,
            new(),
            new(),
            new());

        _broadcastScheduler = new(Substitute.For<IBroadcastMessenger>(),
            new DefaultBroadcastPlatformTargetsProvider(),
            settingsManager,
            bus,
            TimeProvider.System,
            NullLogger<BroadcastScheduler>.Instance);

        var userRepository = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var botRepository = Substitute.For<IBotStatisticsRepository>();
        var fileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _directory);
        var userUnits = UserStatisticsPlatformCatalog.All
            .Select(descriptor => new PlatformUserStatisticsUnit(
                descriptor,
                userRepository.GetPartition(descriptor.Platform),
                fileStore,
                NullLogger<PlatformUserStatisticsUnit>.Instance))
            .ToArray();
        var botUnit = new BotStatisticsPersistenceUnit(botRepository, fileStore, NullLogger<BotStatisticsPersistenceUnit>.Instance);
        var commandUnit = new CommandUsagePersistenceUnit(new(TimeProvider.System), fileStore, NullLogger<CommandUsagePersistenceUnit>.Instance);

        _statisticsAutoSaver = new(
            [.. userUnits, botUnit, commandUnit],
            [.. userUnits, botUnit, commandUnit],
            [],
            NullLogger<StatisticsAutoSaver>.Instance);

        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));

        _eventSubBot = new(PlatformAuthRole.Bot,
            eventSubClient,
            accountsStore,
            bus,
            NullLogger<EventSubConnectionHost>.Instance);

        _eventSubBroadcaster = new(PlatformAuthRole.Broadcaster,
            Substitute.For<ITwitchEventSubClient>(),
            accountsStore,
            bus,
            NullLogger<EventSubConnectionHost>.Instance);

        var history = new BroadcastHistoryStore(null, Path.Combine(_directory, "broadcasts.json"), Path.Combine(_directory, "stream_sessions.json"));
        _activeBroadcastStore = new(history, null, Path.Combine(_directory, "active_broadcast.json"), Path.Combine(_directory, "active_session.json"));
        _source = new(_memoryUsageSink,
            _metrics,
            _eventSubBot,
            _eventSubBroadcaster,
            botConnection,
            _targetChannelProvider,
            chatIngestion,
            _chatSender,
            obs,
            _sse,
            new(),
            _broadcastScheduler,
            _statisticsAutoSaver,
            NullLogger<DiagnosticsSnapshotSource>.Instance,
            _activeBroadcastStore);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventSubBot.DisposeAsync();
        await _eventSubBroadcaster.DisposeAsync();
        await _broadcastScheduler.DisposeAsync();
        await _statisticsAutoSaver.DisposeAsync();
        await _sse.DisposeAsync();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string _directory = null!;
    private EventBusMetrics _metrics = null!;
    private MemoryUsageSink _memoryUsageSink = null!;
    private ChatSender _chatSender = null!;
    private ITargetChannelProvider _targetChannelProvider = null!;
    private SseService _sse = null!;
    private BroadcastScheduler _broadcastScheduler = null!;
    private StatisticsAutoSaver _statisticsAutoSaver = null!;
    private EventSubConnectionHost _eventSubBot = null!;
    private EventSubConnectionHost _eventSubBroadcaster = null!;
    private DiagnosticsSnapshotSource _source = null!;
    private ActiveBroadcastStore _activeBroadcastStore = null!;

    [Test]
    public void Отказ_сохранения_нового_черновика_виден_в_диагностике()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.That(_activeBroadcastStore.TrySave(new()
        {
            BroadcastId = Guid.NewGuid(), StartedAt = now, UpdatedAt = now, EndedAt = now,
        }), Is.True);
        Assert.That(_activeBroadcastStore.TrySave(new()
        {
            BroadcastId = Guid.NewGuid(), StartedAt = now, UpdatedAt = now,
        }), Is.False);

        var status = _source.Capture().Jobs.Single(job => job.Job == ScheduledJob.StatisticsAutoSave);
        Assert.That(status.LastError, Does.Contain("предыдущий эфир"));
    }

    [Test]
    public void Снимок_при_выключенных_подсистемах_собирается_без_исключения()
    {
        Assert.That(() => _source.Capture(), Throws.Nothing);
    }

    [Test]
    public void Упавший_источник_гасит_свою_секцию_и_не_рушит_снимок()
    {
        _targetChannelProvider.Current.Returns(_ => throw new InvalidOperationException("источник упал"));

        var snapshot = _source.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Chat, Is.Null);
            Assert.That(snapshot.Obs, Is.Not.Null);
            Assert.That(snapshot.Sse, Is.Not.Null);
            Assert.That(snapshot.ChatQueue, Is.Not.Null);
            Assert.That(snapshot.Bus, Is.Not.Null);
            Assert.That(snapshot.EventSub, Has.Count.EqualTo(2));
            Assert.That(snapshot.Jobs, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Выключенные_подсистемы_дают_состояния_без_данных_а_не_нули()
    {
        var snapshot = _source.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Memory, Is.Null);
            Assert.That(snapshot.Chat!.State, Is.EqualTo(ConnectionState.Unknown));
            Assert.That(snapshot.Chat.Channel, Is.Null);
            Assert.That(snapshot.Chat.JoinedAt, Is.Null);
            Assert.That(snapshot.Chat.LastMessageAt, Is.Null);
            Assert.That(snapshot.Obs!.State, Is.EqualTo(ConnectionState.Unknown));
            Assert.That(snapshot.Obs.LastEventAt, Is.Null);
            Assert.That(snapshot.Sse!.Running, Is.False);
            Assert.That(snapshot.Sse.ClientCount, Is.Zero);
            Assert.That(snapshot.ChatQueue!.LastSentAt, Is.Null);
            Assert.That(snapshot.Bus, Is.Not.Null);
        });
    }

    [Test]
    public void Соединения_и_работы_попадают_в_снимок_каждое_своей_записью()
    {
        var snapshot = _source.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.EventSub.Select(status => status.Role),
                Is.EquivalentTo(new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster }));

            Assert.That(snapshot.EventSub.All(status => status.State == ConnectionState.Unknown), Is.True);
            Assert.That(snapshot.EventSub.All(status => status.ReconnectAttempt == 0), Is.True);
            Assert.That(snapshot.EventSub.All(status => status.ReconnectLimit > 0), Is.True);

            Assert.That(snapshot.Jobs.Select(job => job.Job),
                Is.EquivalentTo(new[] { ScheduledJob.Broadcast, ScheduledJob.StatisticsAutoSave }));

            Assert.That(snapshot.Jobs.All(job => job.NextRunAt is null), Is.True);
            Assert.That(snapshot.Jobs.All(job => job.LastRunAt is null), Is.True);
            Assert.That(snapshot.Jobs.All(job => job.LastError is null), Is.True);
        });
    }

    [Test]
    public void Очередь_чата_отдаёт_ёмкость_канала_и_нулевые_счётчики_до_отправок()
    {
        var snapshot = _source.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ChatQueue!.Length, Is.Zero);
            Assert.That(snapshot.ChatQueue.Capacity, Is.EqualTo(_chatSender.QueueMaxLength));
            Assert.That(snapshot.ChatQueue.Capacity, Is.GreaterThan(0));
            Assert.That(snapshot.ChatQueue.SentCount, Is.Zero);
            Assert.That(snapshot.ChatQueue.FailedCount, Is.Zero);
        });
    }

    [Test]
    public void Память_приходит_в_снимок_только_после_замера_сторожа()
    {
        Assert.That(_source.Capture().Memory, Is.Null);

        var measured = MemoryWatchdog.Measure(NullLogger.Instance);
        _memoryUsageSink.Publish(measured);

        var memory = _source.Capture().Memory;

        Assert.Multiple(() =>
        {
            Assert.That(memory, Is.SameAs(measured));
            Assert.That(memory!.SelfBytes, Is.GreaterThan(0));
            Assert.That(memory.SelfThresholdBytes, Is.EqualTo(1024L * 1024 * 1024));
            Assert.That(memory.TotalThresholdBytes, Is.EqualTo(2048L * 1024 * 1024));
            Assert.That(memory.TotalBytes, Is.EqualTo(memory.SelfBytes + memory.ChildBytes));
            Assert.That(memory.Children.All(child => !string.IsNullOrWhiteSpace(child.Name)), Is.True);
            Assert.That(memory.Children.All(child => child.Count > 0), Is.True);
            Assert.That(memory.ChildCount, Is.EqualTo(memory.Children.Sum(child => child.Count)));
        });
    }
}
