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
using PoproshaykaBot.Core.YouTube.Quota;

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

        _obsClient = Substitute.For<IObsWebSocketClient>();
        var obs = new ObsIntegrationService(_obsClient,
            settingsManager,
            NullLogger<ObsIntegrationService>.Instance);

        _obsIntegrationStore = new(bus,
            NullLogger<ObsIntegrationStore>.Instance,
            Path.Combine(_directory, "obs-integration.json"));

        var obsChatStore = new ObsChatStore(bus,
            NullLogger<ObsChatStore>.Instance,
            Path.Combine(_directory, "obs-chat.json"));

        _sseDropMetrics = new();
        _sse = new(settingsManager,
            obsChatStore,
            NullLogger<SseService>.Instance,
            new(),
            new(),
            _sseDropMetrics);

        _broadcastScheduler = new(Substitute.For<IBroadcastMessenger>(),
            new DefaultBroadcastPlatformTargetsProvider(),
            settingsManager,
            bus,
            TimeProvider.System,
            NullLogger<BroadcastScheduler>.Instance);

        var userRepository = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var botRepository = Substitute.For<IBotStatisticsRepository>();
        var fileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _directory);
        var userUnit = new UserStatisticsCatalogPersistenceUnit(
            userRepository,
            fileStore,
            new LegacyUserStatisticsMigrator(
                fileStore,
                new KnownChannelIdentityResolver(),
                NullLogger<LegacyUserStatisticsMigrator>.Instance),
            NullLogger<UserStatisticsCatalogPersistenceUnit>.Instance);
        var botUnit = new BotStatisticsPersistenceUnit(botRepository, fileStore, NullLogger<BotStatisticsPersistenceUnit>.Instance);
        var commandUnit = new CommandUsagePersistenceUnit(new(TimeProvider.System), fileStore, NullLogger<CommandUsagePersistenceUnit>.Instance);

        _statisticsAutoSaver = new(
            [userUnit, botUnit, commandUnit],
            [userUnit, botUnit, commandUnit],
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
        _botConnection = botConnection;
        _chatIngestion = chatIngestion;
        _obs = obs;
        _source = CreateSource();
    }

    private DiagnosticsSnapshotSource CreateSource(
        IYouTubeMonitorDiagnostics? youtubeMonitor = null,
        IYouTubeChatDiagnostics? youtubeChat = null,
        IYouTubeQuotaGovernor? youtubeQuota = null) =>
        new(_memoryUsageSink,
            _metrics,
            _eventSubBot,
            _eventSubBroadcaster,
            _botConnection,
            _targetChannelProvider,
            _chatIngestion,
            _chatSender,
            _obs,
            _sse,
            new(),
            _broadcastScheduler,
            _statisticsAutoSaver,
            NullLogger<DiagnosticsSnapshotSource>.Instance,
            _activeBroadcastStore,
            youtubeMonitor,
            youtubeChat,
            youtubeQuota,
            _obsIntegrationStore);

    [TearDown]
    public async Task TearDown()
    {
        await _eventSubBot.DisposeAsync();
        await _eventSubBroadcaster.DisposeAsync();
        await _broadcastScheduler.DisposeAsync();
        await _statisticsAutoSaver.DisposeAsync();
        await _sse.DisposeAsync();
        await _obsClient.DisposeAsync();
        _obs.Dispose();

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
    private IBotConnectionController _botConnection = null!;
    private ChatIngestionService _chatIngestion = null!;
    private ObsIntegrationService _obs = null!;
    private IObsWebSocketClient _obsClient = null!;
    private ObsIntegrationStore _obsIntegrationStore = null!;
    private SseDropMetrics _sseDropMetrics = null!;
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

        var snapshot = _source.Capture();
        var autoSave = snapshot.Jobs.Single(job => job.Job == ScheduledJob.StatisticsAutoSave);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ActiveBroadcastSaveError, Does.Contain("предыдущий эфир"));
            Assert.That(autoSave.LastError, Is.Null);
        });
    }

    [Test]
    public async Task Ошибка_черновика_не_затеняет_ошибку_автосохранения_статистики()
    {
        var failingUnit = Substitute.For<IPeriodicStatisticsUnit>();
        failingUnit.PrepareSave(Arg.Any<bool>()).Returns(_ => throw new IOException("диск статистики занят"));

        await using var failingAutoSaver = new StatisticsAutoSaver(
            [failingUnit],
            [],
            [],
            NullLogger<StatisticsAutoSaver>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(() => failingAutoSaver.SaveNowAsync());

        var now = DateTimeOffset.UtcNow;
        Assert.That(_activeBroadcastStore.TrySave(new()
        {
            BroadcastId = Guid.NewGuid(), StartedAt = now, UpdatedAt = now, EndedAt = now,
        }), Is.True);
        Assert.That(_activeBroadcastStore.TrySave(new()
        {
            BroadcastId = Guid.NewGuid(), StartedAt = now, UpdatedAt = now,
        }), Is.False);

        var source = new DiagnosticsSnapshotSource(
            _memoryUsageSink,
            _metrics,
            _eventSubBot,
            _eventSubBroadcaster,
            _botConnection,
            _targetChannelProvider,
            _chatIngestion,
            _chatSender,
            _obs,
            _sse,
            new(),
            _broadcastScheduler,
            failingAutoSaver,
            NullLogger<DiagnosticsSnapshotSource>.Instance,
            _activeBroadcastStore,
            obsIntegrationStore: _obsIntegrationStore);

        var snapshot = source.Capture();
        var autoSave = snapshot.Jobs.Single(job => job.Job == ScheduledJob.StatisticsAutoSave);

        Assert.Multiple(() =>
        {
            Assert.That(autoSave.LastError, Does.Contain("диск статистики занят"));
            Assert.That(snapshot.ActiveBroadcastSaveError, Does.Contain("предыдущий эфир"));
        });
    }

    [Test]
    public async Task Снимок_передаёт_потери_клиентов_SSE_активность_рассылки_и_детали_OBS()
    {
        _sseDropMetrics.IncrementClientDrops();
        _sseDropMetrics.IncrementClientDrops();

        _obsIntegrationStore.Save(new()
        {
            Enabled = true,
            Password = "super-secret-password",
        });

        _obsClient.ConnectAsync(Arg.Any<ObsConnectionOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ObsConnectionSnapshot>(new InvalidOperationException("неверный пароль OBS")));

        await _obs.ConnectAsync(_obsIntegrationStore.Load(), CancellationToken.None);

        var snapshot = _source.Capture();
        var broadcastJob = snapshot.Jobs.Single(job => job.Job == ScheduledJob.Broadcast);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Sse!.ClientDroppedMessageCount, Is.EqualTo(2));
            Assert.That(broadcastJob.IsActive, Is.False);
            Assert.That(broadcastJob.SentCount, Is.Zero);
            Assert.That(snapshot.Obs!.State, Is.EqualTo(ConnectionState.Failed));
            Assert.That(snapshot.Obs.IsEnabled, Is.True);
            Assert.That(snapshot.Obs.ErrorMessage, Is.EqualTo("неверный пароль OBS"));
            Assert.That(snapshot.Obs.ErrorMessage, Does.Not.Contain("super-secret-password"));
        });
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

    [Test]
    public void Захватывает_снимок_YouTube_мониторинга_чата_и_квоты()
    {
        var now = DateTimeOffset.UtcNow;
        var monitorStatus = new YouTubeMonitoringStatus(
            Enabled: true,
            StreamStatus.Online,
            YouTubeMonitorIssue.MultipleActiveBroadcasts,
            "UCtarget",
            "vid-1",
            "chat-1",
            2,
            now,
            1);
        var chatStatus = new YouTubeChatStatus(
            ConnectionState.Connected,
            YouTubeChatTransport.GrpcStream,
            ChatReadIssue.None,
            "@creator",
            "chat-1",
            now.AddMinutes(-5),
            now.AddSeconds(-2),
            now.AddSeconds(-1),
            42,
            1,
            0);
        var quotaStatus = new YouTubeQuotaStatus(
            "proj-1",
            new DateOnly(2026, 11, 15),
            now.AddHours(4),
            10_000,
            125,
            10,
            25,
            100,
            7_500,
            ExhaustedByServer: false,
            YouTubeQuotaReliability.Reliable,
            CanWriteFile: true);

        var monitor = Substitute.For<IYouTubeMonitorDiagnostics>();
        monitor.CaptureDiagnostics().Returns(monitorStatus);
        var chat = Substitute.For<IYouTubeChatDiagnostics>();
        chat.CaptureDiagnostics().Returns(chatStatus);
        var quota = Substitute.For<IYouTubeQuotaGovernor>();
        quota.CaptureDiagnostics().Returns(quotaStatus);

        var snapshot = CreateSource(monitor, chat, quota).Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.YouTubeMonitoring, Is.EqualTo(monitorStatus));
            Assert.That(snapshot.YouTubeChat, Is.EqualTo(chatStatus));
            Assert.That(snapshot.YouTubeQuota, Is.EqualTo(quotaStatus));
        });
    }

    [Test]
    public void Изолирует_ошибки_источников_диагностики_YouTube_как_null()
    {
        var monitor = Substitute.For<IYouTubeMonitorDiagnostics>();
        monitor.CaptureDiagnostics().Returns(_ => throw new InvalidOperationException("monitor boom"));
        var chat = Substitute.For<IYouTubeChatDiagnostics>();
        chat.CaptureDiagnostics().Returns(_ => throw new InvalidOperationException("chat boom"));
        var quota = Substitute.For<IYouTubeQuotaGovernor>();
        quota.CaptureDiagnostics().Returns(_ => throw new InvalidOperationException("quota boom"));

        var snapshot = CreateSource(monitor, chat, quota).Capture();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.YouTubeMonitoring, Is.Null);
            Assert.That(snapshot.YouTubeChat, Is.Null);
            Assert.That(snapshot.YouTubeQuota, Is.Null);
        });
    }
}
