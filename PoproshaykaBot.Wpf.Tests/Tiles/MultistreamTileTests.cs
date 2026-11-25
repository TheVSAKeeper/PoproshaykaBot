using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;

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
                Assert.That(twitchRow.StatusBadgeText, Is.EqualTo("Отключён"));
                Assert.That(twitchRow.IsDisabled, Is.True);
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
                Assert.That(row.ConnectionActionWithPlatform, Is.EqualTo("Подключить YouTube"));
                Assert.That(row.StatusBadgeText, Is.Null);
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
            var youtube = new FakePlatform(
                StreamingPlatform.YouTube,
                () => youtubeSettings.Load().Enabled && !debug,
                () => youtubeSettings.Load().Enabled && debug);
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
    public async Task Disabled_platforms_show_enable_only_when_settings_controls_support_it()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-disabled-");
        try
        {
            var youtube = new FakePlatform(StreamingPlatform.YouTube, () => false);
            var vkLive = new FakePlatform(StreamingPlatform.VkLive, () => false);
            var registry = new ChatPlatformRegistry([youtube, vkLive]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            using var tile = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, youtubeSettings);
            tile.ApplyPlatformControls([new StubControls(StreamingPlatform.YouTube, canEnableFromSettings: true)]);

            var ytRow = tile.Rows.Single(row => row.Platform == StreamingPlatform.YouTube);
            var vkRow = tile.Rows.Single(row => row.Platform == StreamingPlatform.VkLive);

            Assert.Multiple(() =>
            {
                Assert.That(ytRow.IsDisabled, Is.True);
                Assert.That(ytRow.PlatformActionLabel, Is.EqualTo("Включить"));
                Assert.That(vkRow.IsDisabled, Is.True);
                Assert.That(vkRow.PlatformActionLabel, Is.EqualTo("Настроить"));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class StubControls(StreamingPlatform platform, bool canEnableFromSettings) : PoproshaykaBot.Wpf.ViewModels.IMultistreamPlatformControls
    {
        public StreamingPlatform Platform => platform;
        public bool RequiresCardSetup => canEnableFromSettings;
        public bool CanEnableFromSettings => canEnableFromSettings;
    }

    [TestCase(0, false, null)]
    [TestCase(1, false, null)]
    [TestCase(2, true, "2 канала")]
    [TestCase(3, true, "3 канала")]
    [TestCase(5, true, "5 каналов")]
    [TestCase(11, true, "11 каналов")]
    [TestCase(21, true, "21 канал")]
    public void Row_formats_Russian_channel_count_pill(int count, bool expectedMultiple, string? expectedText)
    {
        var row = new MultistreamPlatformRowViewModel(new FakePlatform(StreamingPlatform.YouTube), () => { }, () => { })
        {
            ChannelCount = count,
        };

        Assert.Multiple(() =>
        {
            Assert.That(row.HasMultipleChannels, Is.EqualTo(expectedMultiple));
            Assert.That(row.ChannelCountText, Is.EqualTo(expectedText));
        });
    }

    [Test]
    public async Task Выбор_канала_в_виджете_переключает_активный_канал_и_не_дёргает_список_по_таймеру()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-switch-");
        try
        {
            var platform = StreamingPlatform.YouTube;
            var youtube = new FakePlatform(platform);
            var platforms = new ChatPlatformRegistry([youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings =>
            {
                settings.Channels.Add(new()
                {
                    Platform = platform,
                    ChannelId = "UC-1",
                    DisplayName = "Основной",
                    IsConfigured = true,
                });
                settings.Channels.Add(new()
                {
                    Platform = platform,
                    ChannelId = "UC-2",
                    DisplayName = "Запасной",
                    IsConfigured = true,
                });
                settings.Platforms.Add(new()
                {
                    Platform = platform,
                    ActiveChannelId = "UC-1",
                });
            }), Is.True);

            var switchGate = new TaskCompletionSource<ChannelSwitchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coordinator = new FakeSwitchCoordinator((_, target) => switchGate.Task.ContinueWith(_ =>
            {
                catalog.Mutate(settings =>
                {
                    var routing = settings.Platforms.First(p => p.Platform == platform);
                    routing.ActiveChannelId = target.ChannelId;
                });
                return new ChannelSwitchResult(ChannelSwitchOutcome.Switched, target);
            }));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(platform)],
                null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings,
                coordinator);
            tile.SetOnScreen(true);

            var row = tile.Rows.Single();
            var statuses = new List<(string Message, StatusSeverity Severity)>();
            tile.StatusReported += (message, severity) => statuses.Add((message, severity));
            var collectionChanges = 0;
            row.Channels.CollectionChanged += (_, _) => collectionChanges++;

            dispatcher.Timers.Single().Tick();
            dispatcher.Timers.Single().Tick();

            Assert.Multiple(() =>
            {
                Assert.That(row.HasChannelSelector, Is.True);
                Assert.That(row.ShowStaticChannelName, Is.False);
                Assert.That(row.CanSwitchChannel, Is.True);
                Assert.That(row.Channels.Select(c => c.ChannelId), Is.EqualTo(new[] { "UC-1", "UC-2" }));
                Assert.That(row.SelectedChannel?.ChannelId, Is.EqualTo("UC-1"));
                Assert.That(collectionChanges, Is.EqualTo(0));
            });

            var targetOption = row.Channels.Single(c => c.ChannelId == "UC-2");
            var switchTask = tile.SwitchChannelAsync(row, targetOption);

            Assert.Multiple(() =>
            {
                Assert.That(row.IsSwitchingChannel, Is.True);
                Assert.That(row.CanSwitchChannel, Is.False);
                Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.False);
                Assert.That(row.ChannelBadgeText, Is.EqualTo("Переключение…"));
            });

            switchGate.SetResult(new(ChannelSwitchOutcome.Switched, new(platform, "UC-2")));
            await switchTask;

            Assert.Multiple(() =>
            {
                Assert.That(row.IsSwitchingChannel, Is.False);
                Assert.That(row.CanSwitchChannel, Is.True);
                Assert.That(row.ToggleConnectionCommand.CanExecute(null), Is.True);
                Assert.That(row.SelectedChannel?.ChannelId, Is.EqualTo("UC-2"));
                Assert.That(row.ChannelName, Is.EqualTo("Запасной"));
                Assert.That(row.HasSwitchError, Is.False);
                Assert.That(row.ChannelBadgeText, Is.EqualTo("2 канала"));
                Assert.That(coordinator.Calls, Is.EqualTo(new[] { new ChannelKey(platform, "UC-2") }));
                Assert.That(statuses.Single().Message, Does.Contain("Запасной"));
                Assert.That(statuses.Single().Severity, Is.EqualTo(StatusSeverity.Info));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Ошибка_переключения_возвращает_выбор_канала_и_показывает_предупреждение()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-switch-fail-");
        try
        {
            var platform = StreamingPlatform.Twitch;
            var twitch = new FakePlatform(platform);
            var platforms = new ChatPlatformRegistry([twitch]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings =>
            {
                settings.Channels.Add(new()
                {
                    Platform = platform,
                    ChannelId = "101",
                    DisplayName = "Дубль",
                    IsConfigured = true,
                });
                settings.Channels.Add(new()
                {
                    Platform = platform,
                    ChannelId = "202",
                    DisplayName = "Дубль",
                    IsConfigured = true,
                });
                settings.Platforms.Add(new()
                {
                    Platform = platform,
                    ActiveChannelId = "101",
                });
            }), Is.True);

            var coordinator = new FakeSwitchCoordinator((_, _) => Task.FromResult(
                new ChannelSwitchResult(ChannelSwitchOutcome.FailedKeptPrevious, new(platform, "101"),
                    "Подтвердите доступ аккаунта бота к каналу «Дубль».")));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings,
                coordinator);
            tile.SetOnScreen(true);

            var row = tile.Rows.Single();
            var statuses = new List<(string Message, StatusSeverity Severity)>();
            tile.StatusReported += (message, severity) => statuses.Add((message, severity));

            Assert.That(row.Channels.Select(c => c.Label), Is.EqualTo(new[] { "Дубль (101)", "Дубль (202)" }));

            row.SelectedChannel = row.Channels[1];

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.Calls, Is.EqualTo(new[] { new ChannelKey(platform, "202") }));
                Assert.That(row.SelectedChannel?.ChannelId, Is.EqualTo("101"));
                Assert.That(row.HasSwitchError, Is.True);
                Assert.That(row.SwitchErrorText, Does.Contain("Подтвердите доступ"));
                Assert.That(row.SwitchErrorSeverity, Is.EqualTo(StatusSeverity.Warning));
                Assert.That(statuses.Single().Severity, Is.EqualTo(StatusSeverity.Warning));
            });

            dispatcher.Timers.Single().Tick();
            Assert.That(row.HasSwitchError, Is.True);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Отладка_Twitch_блокирует_выбор_канала_в_виджете()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-debug-");
        try
        {
            var platform = StreamingPlatform.Twitch;
            var twitch = new FakePlatform(platform);
            var platforms = new ChatPlatformRegistry([twitch]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings =>
            {
                settings.Channels.Add(new() { Platform = platform, ChannelId = "101", DisplayName = "Один", IsConfigured = true });
                settings.Channels.Add(new() { Platform = platform, ChannelId = "202", DisplayName = "Два", IsConfigured = true });
                settings.Platforms.Add(new() { Platform = platform, ActiveChannelId = "101" });
            }), Is.True);

            var coordinator = new FakeSwitchCoordinator((_, target) => Task.FromResult(
                new ChannelSwitchResult(ChannelSwitchOutcome.Switched, target)));
            var debugTarget = new FakeTargetChannelProvider(new("foreign", "one", true, false, false));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings,
                coordinator, debugTarget);
            tile.SetOnScreen(true);

            var row = tile.Rows.Single();
            Assert.Multiple(() =>
            {
                Assert.That(row.HasChannelSelector, Is.True);
                Assert.That(row.CanSwitchChannel, Is.False);
                Assert.That(row.ChannelSelectorTooltip, Does.Contain("отладки"));
            });

            row.SelectedChannel = row.Channels[1];
            Assert.Multiple(() =>
            {
                Assert.That(coordinator.Calls, Is.Empty);
                Assert.That(row.SelectedChannel?.ChannelId, Is.EqualTo("101"));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task Разметка_виджета_показывает_список_каналов_только_при_нескольких_каналах()
    {
        EnsureWpfResources();
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-view-");
        try
        {
            var twitch = new FakePlatform(StreamingPlatform.Twitch);
            var youtube = new FakePlatform(StreamingPlatform.YouTube);
            var platforms = new ChatPlatformRegistry([twitch, youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings =>
            {
                settings.Channels.Add(new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-1", DisplayName = "Мой Twitch", IsConfigured = true });
                settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "yt-1", DisplayName = "Канал А", IsConfigured = true });
                settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "yt-2", DisplayName = "Канал Б", IsConfigured = true });
                settings.Platforms.Add(new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "yt-1" });
            }), Is.True);

            var coordinator = new FakeSwitchCoordinator((_, target) =>
            {
                catalog.Mutate(settings => settings.Platforms.First(p => p.Platform == target.Platform).ActiveChannelId = target.ChannelId);
                return Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, target));
            });
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings,
                coordinator);

            var view = new MultistreamTileView { DataContext = tile };
            view.Measure(new(320, 600));
            view.Arrange(new(0, 0, 320, 600));
            view.UpdateLayout();

            var combos = Descendants<ComboBox>(view).ToArray();
            var twitchCombo = combos.Single(combo => System.Windows.Automation.AutomationProperties.GetAutomationId(combo) == "MultistreamChannelTwitch");
            var youtubeCombo = combos.Single(combo => System.Windows.Automation.AutomationProperties.GetAutomationId(combo) == "MultistreamChannelYouTube");

            Assert.Multiple(() =>
            {
                Assert.That(twitchCombo.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(youtubeCombo.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(youtubeCombo.Items.Count, Is.EqualTo(2));
                Assert.That(((MultistreamChannelOption)youtubeCombo.SelectedItem).ChannelId, Is.EqualTo("yt-1"));
            });

            youtubeCombo.SelectedIndex = 1;
            Assert.That(coordinator.Calls, Is.EqualTo(new[] { new ChannelKey(StreamingPlatform.YouTube, "yt-2") }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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

    [Test]
    public async Task Массовое_подключение_и_отключение_работает_при_двух_готовых_площадках()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-bulk-");

        try
        {
            var youtubeEnabled = true;
            var twitch = new FakePlatform(StreamingPlatform.Twitch);
            var youtube = new FakePlatform(StreamingPlatform.YouTube, () => youtubeEnabled);
            var platforms = new ChatPlatformRegistry([twitch, youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(StreamingPlatform.Twitch), new FakeConnectionAdapter(StreamingPlatform.YouTube)],
                null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings);
            tile.SetOnScreen(true);

            Assert.Multiple(() =>
            {
                Assert.That(tile.SummaryText, Is.EqualTo("Нет подключённых площадок"));
                Assert.That(tile.ShowBulkConnectionAction, Is.True);
                Assert.That(tile.BulkConnectionLabel, Is.EqualTo("Подключить все"));
                Assert.That(tile.BulkConnectionCommand.CanExecute(null), Is.True);
            });

            await tile.BulkConnectionCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(tile.IsBulkConnectionBusy, Is.False);
                Assert.That(tile.SummaryText, Is.EqualTo("Подключено 2 из 2"));
                Assert.That(tile.BulkConnectionLabel, Is.EqualTo("Отключить все"));
                Assert.That(tile.Rows.All(static r => r.LifecyclePhase == BotLifecyclePhase.Connected), Is.True);
            });

            await connection.StopAsync(StreamingPlatform.YouTube);
            dispatcher.Timers.Single().Tick();

            Assert.Multiple(() =>
            {
                Assert.That(tile.SummaryText, Is.EqualTo("Подключено 1 из 2"));
                Assert.That(tile.BulkConnectionLabel, Is.EqualTo("Подключить все"));
            });

            await tile.BulkConnectionCommand.ExecuteAsync(null);
            Assert.That(tile.Rows.All(static r => r.LifecyclePhase == BotLifecyclePhase.Connected), Is.True);

            await tile.BulkConnectionCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(tile.SummaryText, Is.EqualTo("Нет подключённых площадок"));
                Assert.That(tile.BulkConnectionLabel, Is.EqualTo("Подключить все"));
                Assert.That(tile.Rows.All(static r => r.LifecyclePhase == BotLifecyclePhase.Disconnected), Is.True);
            });

            youtubeEnabled = false;
            dispatcher.Timers.Single().Tick();
            Assert.That(tile.ShowBulkConnectionAction, Is.False);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Сводка_и_строки_показывают_зрителей_аптайм_бейджи_режима_и_предупреждение_мониторинга()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-rich-");

        try
        {
            var twitchInfo = new StreamInfo
            {
                Title = "Играем в тест на Twitch",
                ViewerCount = 42,
                StartedAt = DateTime.UtcNow.AddMinutes(-85),
            };
            var youtubeInfo = new StreamInfo
            {
                Title = "Стрим YouTube",
                ViewerCount = 100,
                StartedAt = DateTime.UtcNow.AddMinutes(-15),
            };
            var twitch = new FakePlatform(StreamingPlatform.Twitch)
            {
                StreamStatus = new FakeStreamStatus(StreamStatus.Online, twitchInfo),
            };
            var youtube = new FakePlatform(StreamingPlatform.YouTube)
            {
                StreamStatus = new FakeStreamStatus(StreamStatus.Online, youtubeInfo),
            };
            var platforms = new ChatPlatformRegistry([twitch, youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(settings =>
            {
                settings.Channels.Add(new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-1", DisplayName = "Twitch 1", IsConfigured = true });
                settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "yt-1", DisplayName = "Канал 1", IsConfigured = true });
                settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "yt-2", DisplayName = "Канал 2", IsConfigured = true });
                settings.Platforms.Add(new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = "yt-1",
                    SwitchMode = ChannelSwitchMode.AutoDiscover,
                });
            }), Is.True);

            var youtubeAuth = new FakeAuthorization(StreamingPlatform.YouTube, (role, _) => role == PlatformAuthRole.Bot);
            var authorization = new PlatformAuthRegistry([youtubeAuth]);
            var ytSnapshots = new[]
            {
                new ChannelMonitoringSnapshot(
                    new(StreamingPlatform.YouTube, "yt-1"),
                    "Канал 1",
                    ChannelMonitoringAvailability.Degraded,
                    MonitoringUnavailableReason.QuotaExhausted,
                    "Квота API почти исчерпана",
                    StreamStatus.Online,
                    youtubeInfo,
                    DateTimeOffset.UtcNow,
                    false),
                new ChannelMonitoringSnapshot(
                    new(StreamingPlatform.YouTube, "yt-2"),
                    "Канал 2",
                    ChannelMonitoringAvailability.Active,
                    MonitoringUnavailableReason.None,
                    null,
                    StreamStatus.Online,
                    null,
                    DateTimeOffset.UtcNow,
                    false),
            };
            var monitor = new MultiSnapshotFakeMonitor(StreamingPlatform.YouTube, ytSnapshots);

            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, authorization, [monitor], bus, dispatcher, youtubeSettings);
            tile.SetOnScreen(true);

            var twitchRow = tile.Rows.Single(r => r.Platform == StreamingPlatform.Twitch);
            var ytRow = tile.Rows.Single(r => r.Platform == StreamingPlatform.YouTube);

            Assert.Multiple(() =>
            {
                Assert.That(tile.SummaryText, Is.EqualTo("В эфире: 2 · 142 зр."));
                Assert.That(tile.SummarySeverity, Is.EqualTo(StatusSeverity.Success));
                Assert.That(twitchRow.HasViewerCount, Is.True);
                Assert.That(twitchRow.ViewerCountText, Is.EqualTo("42 зр."));
                Assert.That(twitchRow.HasLiveSummary, Is.True);
                Assert.That(twitchRow.LiveSummaryText, Does.Contain("1 ч 2").And.Contain("Играем в тест на Twitch"));
                Assert.That(ytRow.ViewerCountText, Is.EqualTo("100 зр."));
                Assert.That(ytRow.IsBotOnly, Is.True);
                Assert.That(ytRow.HasSwitchModeBadge, Is.True);
                Assert.That(ytRow.SwitchModeBadgeText, Is.EqualTo("Автопереключение"));
                Assert.That(ytRow.HasBackgroundLive, Is.True);
                Assert.That(ytRow.BackgroundLiveCount, Is.EqualTo(1));
                Assert.That(ytRow.BackgroundLiveText, Is.EqualTo("Ещё 1 в эфире"));
                Assert.That(ytRow.HasContextBadges, Is.True);
                Assert.That(ytRow.HasMonitoringWarning, Is.True);
                Assert.That(ytRow.MonitoringWarningText, Does.Contain("Квота API почти исчерпана"));
            });

            Assert.That(catalog.Mutate(settings =>
            {
                var routing = settings.Platforms.Single(p => p.Platform == StreamingPlatform.YouTube);
                routing.SwitchMode = ChannelSwitchMode.PreselectedNext;
                routing.PreselectedNextChannelId = "yt-2";
            }), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(ytRow.SwitchModeBadgeText, Is.EqualTo("Следующий канал"));
                Assert.That(ytRow.SwitchModeBadgeTooltip, Does.Contain("Канал 2"));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task Отключённый_бот_при_идущем_эфире_выделяется_Warning_и_Primary_кнопкой_без_лишнего_зелёного()
    {
        EnsureWpfResources();
        var directory = Directory.CreateTempSubdirectory("poproshayka-multistream-hierarchy-");

        try
        {
            var twitchInfo = new StreamInfo
            {
                Title = "Стрим на проверку",
                ViewerCount = 3540,
                StartedAt = DateTime.UtcNow.AddMinutes(-45),
            };
            var twitch = new FakePlatform(StreamingPlatform.Twitch)
            {
                StreamStatus = new FakeStreamStatus(StreamStatus.Online, twitchInfo),
            };
            var youtube = new FakePlatform(StreamingPlatform.YouTube)
            {
                StreamStatus = new FakeStreamStatus(StreamStatus.Offline),
            };
            var platforms = new ChatPlatformRegistry([twitch, youtube]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(directory.FullName, "youtube.json"));
            await using var connection = new BotConnectionManager(
                new AppHost([], NullLogger<AppHost>.Instance),
                [new FakeConnectionAdapter(StreamingPlatform.Twitch), new FakeConnectionAdapter(StreamingPlatform.YouTube)],
                null, bus, NullLogger<BotConnectionManager>.Instance);

            using var tile = new MultistreamTileViewModel(
                platforms, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtubeSettings);
            tile.SetOnScreen(true);

            var twitchRow = tile.Rows.Single(r => r.Platform == StreamingPlatform.Twitch);
            var ytRow = tile.Rows.Single(r => r.Platform == StreamingPlatform.YouTube);

            Assert.Multiple(() =>
            {
                Assert.That(twitchRow.ChatText, Is.EqualTo("Чат: не подключён"));
                Assert.That(twitchRow.ChatSeverity, Is.EqualTo(StatusSeverity.Warning));
                Assert.That(twitchRow.IsConnectionAttentionRequired, Is.True);
                Assert.That(ytRow.ChatText, Is.EqualTo("Чат: не подключён"));
                Assert.That(ytRow.ChatSeverity, Is.EqualTo(StatusSeverity.Info));
                Assert.That(ytRow.IsConnectionAttentionRequired, Is.False);
            });

            using var bindingErrors = BindingErrorSink.Attach();
            bindingErrors.Clear();

            var view = new MultistreamTileView { DataContext = tile };
            view.Measure(new(320, 600));
            view.Arrange(new(0, 0, 320, 600));
            view.UpdateLayout();

            var buttons = Descendants<Button>(view).ToArray();
            var twitchConnect = buttons.Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "MultistreamConnectionTwitch");
            var ytConnect = buttons.Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "MultistreamConnectionYouTube");
            var primaryStyle = (Style)Application.Current.FindResource("Button.Primary");
            var secondaryStyle = (Style)Application.Current.FindResource("Button.Secondary");
            var neutralBadgeStyle = (Style)Application.Current.FindResource("Badge");
            var chatBlocks = Descendants<TextBlock>(view).Where(t => t.Name == "ChatStatusText").ToArray();
            var viewerBadges = Descendants<Border>(view).Where(b => b.Name == "ViewerCountBadge").ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(bindingErrors.Count, Is.Zero);
                Assert.That(twitchConnect.Style, Is.SameAs(primaryStyle));
                Assert.That(ytConnect.Style, Is.SameAs(secondaryStyle));
                Assert.That(chatBlocks.Select(t => t.FontSize), Is.All.EqualTo((double)Application.Current.FindResource("Font.Size.Body")));
                Assert.That(chatBlocks.Select(t => t.FontWeight), Is.All.EqualTo(FontWeights.SemiBold));
                Assert.That(viewerBadges[0].Style, Is.SameAs(neutralBadgeStyle));
                Assert.That(view.DesiredSize.Width, Is.LessThanOrEqualTo(320));
            });

            connection.StartConnection(StreamingPlatform.Twitch);
            await connection.WaitForConnectionAsync(StreamingPlatform.Twitch);
            twitch.ChatState = ConnectionState.Connected;
            tile.SetOnScreen(true);
            dispatcher.Timers.Single().Tick();
            view.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(twitchRow.ChatText, Is.EqualTo("Чат: подключён"));
                Assert.That(twitchRow.ChatSeverity, Is.EqualTo(StatusSeverity.Success));
                Assert.That(twitchRow.IsConnectionAttentionRequired, Is.False);
                Assert.That(twitchConnect.Style, Is.SameAs(secondaryStyle));
                Assert.That(bindingErrors.Count, Is.Zero);
            });
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

    private sealed class FakePlatform(
        StreamingPlatform id,
        Func<bool>? isEnabled = null,
        Func<bool>? isBlockedByDebugSession = null) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => id.ToString();
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled?.Invoke() ?? true;
        public bool IsBlockedByDebugSession => isBlockedByDebugSession?.Invoke() ?? false;
        public bool UsesChannelCatalog { get; init; } = true;
        public string? ConfiguredChannel { get; init; }
        public bool IsConnected => ChatState == ConnectionState.Connected;
        public IStreamStatus StreamStatus { get; init; } = new FakeStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
        public ConnectionState ChatState { get; set; } = ConnectionState.Disconnected;
        public ChatReadIssue ChatIssue { get; set; }

        public ChatConnectionStatus CaptureChatStatus(BotLifecyclePhase phase) => new(ChatState, null, null, null, ChatIssue);
    }

    private sealed class FakeStreamStatus(StreamStatus status = StreamStatus.Offline, StreamInfo? stream = null) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;
        public StreamInfo? CurrentStream => stream;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, stream, null);
        public Task RefreshLiveSnapshotAsync() => throw new NotSupportedException();
    }

    private sealed class MultiSnapshotFakeMonitor(
        StreamingPlatform platform,
        IReadOnlyList<ChannelMonitoringSnapshot> snapshots) : IPlatformChannelMonitor
    {
        public StreamingPlatform Platform => platform;
        public string Name => "Монитор трансляций";
        public int StartOrder => 0;
        public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel) =>
            snapshots.FirstOrDefault(s => s.Channel == channel)
            ?? new(channel, channel.ChannelId, ChannelMonitoringAvailability.Active, MonitoringUnavailableReason.None, null, StreamStatus.Offline, null, null, false);
        public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots() => snapshots;
        public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class FakeTargetChannelProvider(TargetChannelState current) : ITargetChannelProvider
    {
        public TargetChannelState Current => current;
        public void BeginSession() { }
        public void EndSession() { }
        public void SetSessionLogin(string login) { }
    }

    private sealed class FakeSwitchCoordinator(
        Func<StreamingPlatform, ChannelKey, Task<ChannelSwitchResult>> onSwitch) : IChannelSwitchCoordinator
    {
        public List<ChannelKey> Calls { get; } = [];

        public AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform) => null;
        public IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts() => [];

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel, CancellationToken cancellationToken = default)
        {
            Calls.Add(targetChannel);
            return onSwitch(platform, targetChannel);
        }

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel, bool preservePreselectedNext, CancellationToken cancellationToken = default) =>
            SwitchActiveChannelAsync(platform, targetChannel, cancellationToken);

        public Task<ChannelSwitchResult> SwitchCatalogChannelAsync(ChannelCatalogEntry targetChannel, CancellationToken cancellationToken = default) =>
            SwitchActiveChannelAsync(targetChannel.Platform, targetChannel.Key, cancellationToken);

        public Task<ChannelSwitchResult?> SetPreselectedNextAsync(StreamingPlatform platform, ChannelKey? targetChannel, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChannelSwitchResult?>(null);

        public Task SetSwitchModeAsync(StreamingPlatform platform, ChannelSwitchMode mode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(StreamingPlatform platform, ChannelKey candidateChannel, CancellationToken cancellationToken = default) =>
            SwitchActiveChannelAsync(platform, candidateChannel, cancellationToken);

        public Task DismissAutoDiscoverPromptAsync(StreamingPlatform platform, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EvaluateStartupPendingAsync(StreamingPlatform platform, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static void EnsureWpfResources()
    {
        TestResources.Ensure(
        [
            "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
            "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
            "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
            "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
        ]);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
