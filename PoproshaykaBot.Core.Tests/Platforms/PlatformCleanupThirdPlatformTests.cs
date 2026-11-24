using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net.Http;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Platforms;

[TestFixture]
public sealed class PlatformCleanupThirdPlatformTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pb-1c-core-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void ChannelCatalogReconciler_reconciles_and_mirrors_third_platform_via_seed_provider()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        var mirrored = new List<ChannelCatalogEntry>();
        var provider = new StubSeedProvider(
            StreamingPlatform.VkLive,
            new PlatformChannelSeed(
                StreamingPlatform.VkLive,
                "vk-42",
                "VK Канал",
                "vk_handle",
                "https://live.vkvideo.ru/vk_handle",
                MonitoringEnabled: true,
                BroadcasterSubjectId: "vk-owner",
                BotSubjectId: "vk-bot"),
            mirrored.Add);

        var reconciler = new ChannelCatalogReconciler(
            catalog,
            [provider],
            NullLogger<ChannelCatalogReconciler>.Instance);

        reconciler.ReconcileStartup();

        var configured = catalog.GetConfiguredChannels(StreamingPlatform.VkLive);
        Assert.Multiple(() =>
        {
            Assert.That(configured, Has.Count.EqualTo(1));
            Assert.That(configured[0].ChannelId, Is.EqualTo("vk-42"));
            Assert.That(configured[0].DisplayName, Is.EqualTo("VK Канал"));
            Assert.That(configured[0].BroadcasterVerifiedSubjectId, Is.EqualTo("vk-owner"));
            Assert.That(configured[0].BotVerifiedSubjectId, Is.EqualTo("vk-bot"));
        });

        var updatedEntry = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.VkLive,
            ChannelId = "vk-99",
            DisplayName = "Новый VK",
        };
        reconciler.MirrorActiveChannelToLegacySettings(updatedEntry);

        Assert.That(mirrored, Has.Count.EqualTo(1));
        Assert.That(mirrored[0].ChannelId, Is.EqualTo("vk-99"));
    }

    [Test]
    public void KnownChannelIdentityResolver_resolves_third_platform_aliases_and_direct_ids()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        var source = new StubIdentitySource(
            StreamingPlatform.VkLive,
            configuredChannel: "https://live.vkvideo.ru/vk-77",
            directIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://live.vkvideo.ru/vk-77"] = "vk-77",
            },
            knownId: "vk-77",
            aliases: ["VK Streamer", "@vk_handle"]);

        var resolver = new KnownChannelIdentityResolver(catalog, [source]);
        var snapshot = resolver.BuildSnapshot(history: null, activeBroadcast: null);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Resolve(StreamingPlatform.VkLive, null, "https://live.vkvideo.ru/vk-77"),
                Is.EqualTo(new ChannelKey(StreamingPlatform.VkLive, "vk-77")));
            Assert.That(snapshot.Resolve(StreamingPlatform.VkLive, null, "vk_handle"),
                Is.EqualTo(new ChannelKey(StreamingPlatform.VkLive, "vk-77")));
            Assert.That(snapshot.GetSingleConfiguredChannel(StreamingPlatform.VkLive),
                Is.EqualTo(new ChannelKey(StreamingPlatform.VkLive, "vk-77")));
        });
    }

    [Test]
    public void StreamingErrorMessages_uses_display_name_for_third_platform()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StreamingErrorMessages.SafeMessage(new TimeoutException(), StreamingPlatform.VkLive),
                Is.EqualTo("Превышено время ожидания ответа VK Live"));
            Assert.That(StreamingErrorMessages.SafeMessage(new HttpRequestException("err"), StreamingPlatform.VkLive),
                Is.EqualTo("Ошибка сети при обращении к VK Live"));
            Assert.That(StreamingErrorMessages.SafeMessage(new UnauthorizedAccessException(), StreamingPlatform.VkLive),
                Is.EqualTo("Ошибка авторизации в VK Live"));
            Assert.That(StreamingErrorMessages.SafeMessage(new JsonException(), StreamingPlatform.VkLive),
                Is.EqualTo("Некорректный ответ VK Live"));
        });
    }

    [Test]
    public void ChatHistoryScope_and_ObsOverlayMessageFilter_respect_session_scope_for_third_platform()
    {
        var vkOfflineStatus = Substitute.For<IStreamStatus>();
        vkOfflineStatus.CurrentStatus.Returns(StreamStatus.Offline);
        var vkPlatform = Substitute.For<IChatPlatform>();
        vkPlatform.Id.Returns(StreamingPlatform.VkLive);
        vkPlatform.StreamStatus.Returns(vkOfflineStatus);
        var registry = new ChatPlatformRegistry([vkPlatform]);

        var sessionMsg = new ChatMessageData
        {
            Platform = StreamingPlatform.VkLive,
            Channel = "vk-chan",
            ChannelId = "vk-1",
            ChatSessionId = "vk-session-1",
            MessageId = "m-1",
            Timestamp = DateTime.UtcNow,
            Message = "Сообщение эфира",
        };

        var scope = ChatHistoryScope.FromMessage(sessionMsg);
        Assert.Multiple(() =>
        {
            Assert.That(scope.Platform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(scope.SessionScopeId, Is.EqualTo("vk-session-1"));
            Assert.That(ObsOverlayMessageFilter.AllowsCurrentStream(sessionMsg, registry), Is.False);
        });

        vkOfflineStatus.CurrentStatus.Returns(StreamStatus.Online);
        vkOfflineStatus.CurrentStream.Returns(new StreamInfo { Id = "vk-stream-1", StartedAt = DateTime.UtcNow.AddMinutes(-5) });
        var liveMsg = new ChatMessageData
        {
            Platform = StreamingPlatform.VkLive,
            Channel = "vk-chan",
            ChannelId = "vk-1",
            StreamId = "vk-stream-1",
            MessageId = "m-2",
            Timestamp = DateTime.UtcNow,
            Message = "Живое сообщение",
        };
        Assert.That(ObsOverlayMessageFilter.AllowsCurrentStream(liveMsg, registry), Is.True);
    }

    [Test]
    public void DiagnosticsSnapshotSource_captures_third_platform_chat_status_from_registry()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(new AppSettings());

        var eventSubClient = Substitute.For<ITwitchEventSubClient>();
        var helix = Substitute.For<ITwitchHelixClient>();
        var broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        var botUserIdProvider = Substitute.For<IBotUserIdProvider>();
        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState(string.Empty, string.Empty, false, false));

        var botConnection = Substitute.For<IBotConnectionController>();
        botConnection.GetPhase(StreamingPlatform.VkLive).Returns(BotLifecyclePhase.Connected);

        var vkChatStatus = new ChatConnectionStatus(ConnectionState.Connected, "vk-chan", DateTimeOffset.UtcNow, null);
        var vkPlatform = Substitute.For<IChatPlatform>();
        vkPlatform.Id.Returns(StreamingPlatform.VkLive);
        vkPlatform.DisplayName.Returns("VK Live");
        vkPlatform.CaptureChatStatus(BotLifecyclePhase.Connected).Returns(vkChatStatus);

        var accountsStore = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        var fileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _root);
        var userUnit = new UserStatisticsCatalogPersistenceUnit(
            new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance),
            fileStore,
            new LegacyUserStatisticsMigrator(fileStore, new KnownChannelIdentityResolver(), NullLogger<LegacyUserStatisticsMigrator>.Instance),
            NullLogger<UserStatisticsCatalogPersistenceUnit>.Instance);
        var autoSaver = new StatisticsAutoSaver([userUnit], [userUnit], [], NullLogger<StatisticsAutoSaver>.Instance);
        var obsClient = Substitute.For<IObsWebSocketClient>();

        var source = new DiagnosticsSnapshotSource(
            new(),
            new(),
            new(PlatformAuthRole.Bot, eventSubClient, accountsStore, bus, NullLogger<EventSubConnectionHost>.Instance),
            new(PlatformAuthRole.Broadcaster, eventSubClient, accountsStore, bus, NullLogger<EventSubConnectionHost>.Instance),
            botConnection,
            target,
            new ChatIngestionService(eventSubClient, helix, broadcasterIdProvider, botUserIdProvider, settingsManager, bus, TimeProvider.System, NullLogger<ChatIngestionService>.Instance),
            new ChatSender(helix, broadcasterIdProvider, botUserIdProvider, target, new(TimeProvider.System), NullLogger<ChatSender>.Instance),
            new ObsIntegrationService(obsClient, settingsManager, NullLogger<ObsIntegrationService>.Instance),
            new SseService(settingsManager, new(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_root, "obs-chat.json")), NullLogger<SseService>.Instance, new(), new(), new()),
            new SseClientRegistry(),
            new BroadcastScheduler(Substitute.For<IBroadcastMessenger>(), new DefaultBroadcastPlatformTargetsProvider(), settingsManager, bus, TimeProvider.System, NullLogger<BroadcastScheduler>.Instance),
            autoSaver,
            NullLogger<DiagnosticsSnapshotSource>.Instance,
            platformRegistry: new ChatPlatformRegistry([vkPlatform]));

        var snapshot = source.Capture();

        Assert.That(snapshot.PlatformChats, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.PlatformChats![0].Platform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(snapshot.PlatformChats[0].DisplayName, Is.EqualTo("VK Live"));
            Assert.That(snapshot.PlatformChats[0].Status, Is.EqualTo(vkChatStatus));
        });
    }

    private sealed class StubSeedProvider(
        StreamingPlatform platform,
        PlatformChannelSeed? seed,
        Action<ChannelCatalogEntry> onMirror) : IPlatformChannelSeedProvider
    {
        public StreamingPlatform Platform => platform;

        public PlatformChannelSeed? TryCreateSeed() => seed;

        public void MirrorActiveChannel(ChannelCatalogEntry entry) => onMirror(entry);
    }

    private sealed class StubIdentitySource(
        StreamingPlatform platform,
        string? configuredChannel,
        IReadOnlyDictionary<string, string> directIds,
        string knownId,
        string[] aliases) : IPlatformChannelIdentitySource
    {
        public StreamingPlatform Platform => platform;

        public void CollectKnownChannels(Action<string, IEnumerable<string?>> registerChannel) =>
            registerChannel(knownId, aliases);

        public string? GetConfiguredChannelRaw() => configuredChannel;

        public bool TryExtractDirectChannelId(string rawChannel, out string channelId) =>
            directIds.TryGetValue(rawChannel, out channelId!);
    }
}
