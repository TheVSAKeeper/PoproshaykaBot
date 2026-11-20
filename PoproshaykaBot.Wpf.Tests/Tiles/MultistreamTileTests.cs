using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class MultistreamTileTests
{
    [TestCase(StreamStatus.Online, StreamStatus.Offline, "Эфир: идёт")]
    [TestCase(StreamStatus.Unknown, StreamStatus.Online, "Эфир: идёт")]
    [TestCase(StreamStatus.Unknown, StreamStatus.Unknown, "Эфир: мониторинг выключен")]
    public async Task Эфир_виден_без_авторизации_бота_и_чат_обновляется_после_входа(
        StreamStatus monitoredStatus, StreamStatus activeStatus, string expectedBroadcast)
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-");

        try
        {
            var platform = StreamingPlatform.YouTube;
            var youtube = new FakePlatform(platform) { StreamStatus = new FakeStreamStatus(activeStatus) };
            var twitch = new FakePlatform(StreamingPlatform.Twitch, () => false);
            var platforms = new ChatPlatformRegistry([twitch, youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            var youtubeAccounts = new YouTubeAccountsStore(filePath: Path.Combine(directory.FullName, "youtube-accounts.json"));
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            var youtubeAuth = new FakeAuthorization(platform, (role, contextId) =>
            {
                var account = youtubeAccounts.Load(role, contextId);
                return PlatformAuthRoles.HasCredentials(account.AccessToken, account.RefreshToken);
            });
            var authorization = new PlatformAuthRegistry([youtubeAuth]);
            var dispatcher = new ManualUiDispatcher();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(platform)],
                null, bus, NullLogger<BotConnectionManager>.Instance);

            Assert.That(catalog.Mutate(settings => settings.Channels.Add(new()
            {
                Platform = platform,
                ChannelId = "UC-live",
                DisplayName = "Канал ведущего",
                IsConfigured = true,
                BotContextId = "channel-bot",
            })), Is.True);
            using var tile = new MultistreamTileViewModel(platforms, catalog, connection, authorization,
                [new FakeMonitor(monitoredStatus)], bus, dispatcher, youtubeSettings);
            tile.SetOnScreen(true);
            var timer = dispatcher.Timers.Single();
            var row = tile.Rows.Single(item => item.Platform == platform);
            var twitchRow = tile.Rows.Single(item => item.Platform == StreamingPlatform.Twitch);
            var platformRequested = new List<StreamingPlatform>();
            var connectionRequested = new List<StreamingPlatform>();
            tile.PlatformRequested += (_, requested) => platformRequested.Add(requested);
            tile.ConnectionToggleRequested += (_, requested) => connectionRequested.Add(requested);

            row.OpenPlatformCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(tile.Rows.Select(item => item.Platform), Is.EqualTo(new[] { StreamingPlatform.Twitch, platform }));
                Assert.That(row.ChannelName, Is.EqualTo("Канал ведущего"));
                Assert.That(row.BroadcastText, Is.EqualTo(expectedBroadcast));
                Assert.That(row.ChatText, Is.EqualTo("Чат: нужна авторизация бота"));
                Assert.That(row.PlatformActionLabel, Is.EqualTo("Войти как бот"));
                Assert.That(row.IsSetupRequired, Is.True);
                Assert.That(row.ShowConnectionAction, Is.False);
                Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.False);
                Assert.That(twitchRow.ChatText, Is.EqualTo("Чат: площадка выключена"));
                Assert.That(twitchRow.PlatformActionLabel, Is.EqualTo("Настроить"));
                Assert.That(twitchRow.IsSetupRequired, Is.True);
                Assert.That(twitchRow.ShowConnectionAction, Is.False);
                Assert.That(platformRequested, Is.EqualTo(new[] { platform }));
                Assert.That(timer.IsRunning, Is.True);
            });

            Assert.That(youtubeAccounts.SaveAll(string.Empty, new YouTubeAccountSettings { RefreshToken = "other-token" }, new()), Is.True);
            await bus.PublishAsync(new PlatformAuthorizationRefreshed(platform, PlatformAuthRole.Bot));
            Assert.That(row.NeedsBotAuthorization, Is.True);

            Assert.That(youtubeAccounts.UpsertContext(PlatformAuthRole.Bot, "channel-bot", "Бот канала",
                new YouTubeAccountSettings { RefreshToken = "test-token" }), Is.True);
            await bus.PublishAsync(new PlatformAuthorizationRefreshed(platform, PlatformAuthRole.Bot, "channel-bot"));
            Assert.Multiple(() =>
            {
                Assert.That(row.PlatformActionLabel, Is.EqualTo("Настроить"));
                Assert.That(row.ConnectionActionLabel, Is.EqualTo("Подключить"));
                Assert.That(row.IsSetupRequired, Is.False);
                Assert.That(row.ShowConnectionAction, Is.True);
                Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.True);
                Assert.That(row.ChatText, Is.EqualTo("Чат: не подключён"));
            });
            row.ToggleConnectionCommand.Execute(null);
            Assert.That(connectionRequested, Is.EqualTo(new[] { platform }));

            connection.StartConnection(platform);
            await connection.WaitForConnectionAsync(platform);
            youtube.ChatState = ConnectionState.Connected;
            timer.Tick();

            Assert.Multiple(() =>
            {
                Assert.That(row.ChatText, Is.EqualTo("Чат: подключён"));
                Assert.That(row.BroadcastText, Is.EqualTo(expectedBroadcast));
                Assert.That(row.ConnectionActionLabel, Is.EqualTo("Отключить"));
            });

            tile.SetPageOnScreen(true);
            tile.SetOnScreen(false);
            youtube.ChatState = ConnectionState.Reconnecting;
            timer.Tick();
            Assert.Multiple(() =>
            {
                Assert.That(timer.IsRunning, Is.True);
                Assert.That(row.ChatText, Is.EqualTo("Чат: переподключается…"));
            });

            youtube.ChatState = ConnectionState.Connected;
            tile.SetOnScreen(true);
            Assert.That(row.ChatText, Is.EqualTo("Чат: подключён"));
            tile.SetPageOnScreen(false);
            Assert.That(timer.IsRunning, Is.True);

            tile.SetOnScreen(false);
            youtube.ChatState = ConnectionState.Reconnecting;
            timer.Tick();
            Assert.Multiple(() =>
            {
                Assert.That(timer.IsRunning, Is.False);
                Assert.That(row.ChatText, Is.EqualTo("Чат: подключён"));
            });
            tile.SetPageOnScreen(true);
            Assert.That(row.ChatText, Is.EqualTo("Чат: переподключается…"));
            tile.Dispose();
            Assert.That(timer.IsRunning, Is.False);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestCase(true, "Канал из каталога")]
    [TestCase(false, "Текущий канал")]
    public async Task Зарегистрированная_площадка_работает_без_сведений_об_авторизации(bool usesCatalog, string expectedChannel)
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-");

        try
        {
            const StreamingPlatform third = StreamingPlatform.VkLive;
            var platform = new FakePlatform(third) { UsesChannelCatalog = usesCatalog, ConfiguredChannel = "Текущий канал" };
            var registry = new ChatPlatformRegistry([platform]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings => settings.Channels.Add(new()
            {
                Platform = third,
                ChannelId = "vk-channel",
                DisplayName = "Канал из каталога",
                IsConfigured = true,
            })), Is.True);
            var auth = new PlatformAuthRegistry(usesCatalog ? [] : [new FakeAuthorization(third)]);
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(third)],
                null, bus, NullLogger<BotConnectionManager>.Instance);
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            using var tile = new MultistreamTileViewModel(registry, catalog, connection, auth, [], bus, dispatcher, youtubeSettings);
            tile.SetOnScreen(true);
            var row = tile.Rows.Single();
            var toggled = new List<StreamingPlatform>();
            var opened = new List<StreamingPlatform>();
            tile.ConnectionToggleRequested += (_, id) => toggled.Add(id);
            tile.PlatformRequested += (_, id) => opened.Add(id);

            Assert.Multiple(() =>
            {
                Assert.That(row.ChannelName, Is.EqualTo(expectedChannel));
                Assert.That(row.NeedsBotAuthorization, Is.False);
                Assert.That(row.ShowConnectionAction, Is.True);
                Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.True);
            });
            row.ToggleConnectionCommand.Execute(null);
            row.OpenPlatformCommand.Execute(null);
            Assert.That(toggled, Is.EqualTo(new[] { third }));
            Assert.That(opened, Is.EqualTo(new[] { third }));

            platform.ChatIssue = ChatReadIssue.MissingBotAuth;
            dispatcher.Timers.Single().Tick();
            Assert.That(row.PlatformActionLabel, Is.EqualTo("Войти как бот"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Настройки_YouTube_обновляют_видимую_строку_без_таймера()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-");

        try
        {
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            var debug = false;
            var youtube = new FakePlatform(StreamingPlatform.YouTube, () => youtubeSettings.Load().Enabled && !debug);
            var registry = new ChatPlatformRegistry([youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(StreamingPlatform.YouTube)],
                null, bus, NullLogger<BotConnectionManager>.Instance);
            using var tile = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, youtubeSettings);
            tile.SetPageOnScreen(true);
            var row = tile.Rows.Single();

            Assert.That(row.ShowConnectionAction, Is.False);
            Assert.That(youtubeSettings.Mutate(settings => settings.Enabled = true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(row.ShowConnectionAction, Is.True);
                Assert.That(row.IsBlockedByDebugSession, Is.False);
                Assert.That(row.ChatText, Is.EqualTo("Чат: не подключён"));
            });

            debug = true;
            dispatcher.Timers.Single().Tick();
            Assert.Multiple(() =>
            {
                Assert.That(row.IsBlockedByDebugSession, Is.True);
                Assert.That(row.PlatformActionLabel, Is.EqualTo("Подробнее"));
                Assert.That(row.ShowConnectionAction, Is.False);
                Assert.That(row.ChatText, Is.EqualTo("Чат: недоступен в режиме отладки Twitch"));
                Assert.That(row.BroadcastText, Is.EqualTo("Эфир: недоступен в режиме отладки Twitch"));
            });

            tile.Dispose();
            Assert.That(youtubeSettings.Mutate(settings => settings.Enabled = false), Is.True);
            Assert.That(row.IsBlockedByDebugSession, Is.True);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void Отключение_видно_при_необходимости_настройки()
    {
        var row = new MultistreamPlatformRowViewModel(new FakePlatform(StreamingPlatform.YouTube), () => { }, () => { });

        row.ApplyChat(enabled: false, blockedByDebug: false, BotLifecyclePhase.Disconnecting, null, lacksBotAuthorization: false);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsSetupRequired, Is.True);
            Assert.That(row.ShowConnectionAction, Is.True);
            Assert.That(row.ConnectionActionLabel, Is.EqualTo("Отключается…"));
            Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void StreamInfoTile_показывает_ID_активного_канала_без_имени()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-");

        try
        {
            var platform = new FakePlatform(StreamingPlatform.YouTube) { ConfiguredChannel = "Другой канал" };
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings => settings.Channels.Add(new()
            {
                Platform = platform.Id,
                ChannelId = "UC-active",
                DisplayName = " ",
                IsConfigured = true,
            })), Is.True);
            using var tile = new StreamInfoTileViewModel(new ChatPlatformRegistry([platform]),
                new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance), NullLogger<StreamInfoTileViewModel>.Instance,
                new ManualUiDispatcher(), new NoOpLauncher(), catalogStore: catalog);

            Assert.That(tile.ActiveChannelDisplayName, Is.EqualTo("UC-active"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class NoOpLauncher : IShellLauncher
    {
        public bool Open(string target) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class FakePlatform(StreamingPlatform id, Func<bool>? isEnabled = null) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => id.ToString();
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled?.Invoke() ?? true;
        public bool UsesChannelCatalog { get; init; } = true;
        public string? ConfiguredChannel { get; init; }
        public bool IsConnected => ChatState == ConnectionState.Connected;
        public IStreamStatus StreamStatus { get; init; } = new FakeStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
        public ConnectionState ChatState { get; set; } = ConnectionState.Disconnected;
        public ChatReadIssue ChatIssue { get; set; }

        public ChatConnectionStatus CaptureChatStatus(BotLifecyclePhase phase) => new(ChatState, null, null, null, ChatIssue);
    }

    private sealed class FakeStreamStatus(StreamStatus status = StreamStatus.Offline) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => throw new NotSupportedException();
    }

    private sealed class FakeMonitor(StreamStatus status) : IPlatformChannelMonitor
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;
        public string Name => "Монитор трансляций";
        public int StartOrder => 0;
        public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel) => new(channel, channel.ChannelId,
            status == StreamStatus.Unknown ? ChannelMonitoringAvailability.Disabled : ChannelMonitoringAvailability.Active,
            status == StreamStatus.Unknown ? MonitoringUnavailableReason.MonitoringDisabled : MonitoringUnavailableReason.None,
            null, status, null, null, false);
        public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots() => [];
        public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAuthorization(
        StreamingPlatform platform,
        Func<PlatformAuthRole, string?, bool>? hasMaterial = null) : IPlatformAuthService
    {
        public StreamingPlatform Platform => platform;
        public bool? HasAuthorizationMaterial(PlatformAuthRole role, string? contextId) => hasMaterial?.Invoke(role, contextId);
        public bool HasUsableAccessToken(PlatformAuthRole role) => throw new NotSupportedException();
        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => throw new NotSupportedException();
        public void ResetRetryState(PlatformAuthRole role) => throw new NotSupportedException();
        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeConnectionAdapter(StreamingPlatform platform) : IPlatformConnectionAdapter
    {
        public StreamingPlatform Platform => platform;
        public bool IsEnabled => true;
        public PlatformHost Host { get; } = new(platform, [], NullLogger<PlatformHost>.Instance);
        public Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.FromResult("UC-live");
        public void EndSession() { }
    }
}
