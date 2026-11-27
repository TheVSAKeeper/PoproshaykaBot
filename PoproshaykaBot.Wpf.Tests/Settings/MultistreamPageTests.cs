using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.GameArt;
using KeepShell.Updates;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class MultistreamPageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Registered_platform_uses_registry_name_and_catalog_without_optional_controls(bool includeTwitch)
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-page-");
        try
        {
            var platform = StreamingPlatform.VkLive;
            var additional = new FakePlatform(platform, "VK Видео Live");
            IChatPlatform[] registeredPlatforms = includeTwitch ? [new FakePlatform(StreamingPlatform.Twitch, "Twitch"), additional] : [additional];
            var registry = new ChatPlatformRegistry(registeredPlatforms);
            var gate = new SettingsWriteGate();
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            Assert.That(catalog.Save(new()
            {
                Channels =
                [
                    new() { Platform = platform, ChannelId = "one", DisplayName = "Первый", IsConfigured = true },
                    new() { Platform = platform, ChannelId = "two", DisplayName = "Активный", IsConfigured = true },
                ],
                Platforms = [new() { Platform = platform, ActiveChannelId = "two" }],
            }), Is.True);
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            using var settings = new SettingsPageViewModel([new ChannelsSettingsSectionContributor(editor, groups, catalog)],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance,
                DispatchProxy.Create<IDialogService, NoDialog>(), new MemorySettings(), platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [],
                null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, new ManualUiDispatcher(), new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json")));
            var youTubeControls = new YouTubeMultistreamPlatformControls(settings);
            var page = new MultistreamPageViewModel(settings, status, [youTubeControls]);
            Assert.That(page.SelectedPlatform, Is.EqualTo(registeredPlatforms[0].Id));

            page.OpenPlatform(platform);
            page.OpenPlatform(StreamingPlatform.YouTube);

            Assert.Multiple(() =>
            {
                Assert.That(page.PageTitle, Is.EqualTo("Каналы и профили"));
                Assert.That(page.UnsavedChangesSubject, Does.Contain("Каналы и профили"));
                Assert.That(page.Cards.Select(card => card.Status.Platform), Is.EqualTo(registeredPlatforms.Select(platform => platform.Id)));
                Assert.That(youTubeControls.Settings, Is.Null);
                Assert.That(page.Cards.Single(card => card.Status.Platform == platform).Controls, Is.Null);
                Assert.That(page.SelectedPlatform, Is.EqualTo(platform));
                Assert.That(page.SelectedPlatformChannelCount, Is.EqualTo(2));
                Assert.That(page.HasSelectedPlatformChannels, Is.True);
                Assert.That(page.HasStep1, Is.False);
                Assert.That(page.HasStep2, Is.False);
                Assert.That(page.EditorTitle, Is.EqualTo("Каналы VK Видео Live"));
                Assert.That(page.HasPlatformSettings, Is.False);
                Assert.That(editor.SelectedPlatform, Is.EqualTo(platform));
                Assert.That(editor.SelectedChannel?.ChannelId, Is.EqualTo("two"));
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Channels));
            });

            page.OpenPlatformFromOverview(platform);
            Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Channels));
            page.OpenAutomation();
            Assert.Multiple(() =>
            {
                Assert.That(page.IsAutomationSelected, Is.True);
                Assert.That(page.IsAutomationExpanded, Is.True);
                Assert.That(page.IsPlatformSelected, Is.False);
                Assert.That(page.SelectedPlatformCard, Is.Null);
                Assert.That(page.Cards.All(c => !c.IsSelected), Is.True);
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Automation));
            });

            page.SelectPlatformCommand.Execute(page.Cards[0]);
            Assert.Multiple(() =>
            {
                Assert.That(page.IsAutomationSelected, Is.False);
                Assert.That(page.IsPlatformSelected, Is.True);
                Assert.That(page.SelectedPlatformCard, Is.SameAs(page.Cards[0]));
                Assert.That(page.Cards[0].IsSelected, Is.True);
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Disabled_youtube_opens_parameters_tab_and_switches_tabs_and_automation_blocks_cleanly()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-yt-");
        try
        {
            var twitch = new FakePlatform(StreamingPlatform.Twitch, "Twitch");
            var youtube = new FakePlatform(StreamingPlatform.YouTube, "YouTube", isEnabled: () => false);
            var registry = new ChatPlatformRegistry([twitch, youtube]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "accounts.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

            var oauth = new OAuthSettingsViewModel(
                DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
                accounts, dialogs, DispatchProxy.Create<IShellLauncher, InertProxy>(),
                DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
                dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            var profilesSection = new BroadcastProfilesSettingsSectionViewModel(null!);

            using var settings = new SettingsPageViewModel(
                [
                    new BasicSettingsSectionContributor(new(), new(), new(), manager, new StubTargetChannelProvider()),
                    new TwitchConnectionSettingsContributor(oauth, accounts, manager, NullLogger<TwitchConnectionSettingsContributor>.Instance),
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new StreamSettingsSectionContributor(new(), new(), new(new(filePath: Path.Combine(data.FullName, "polls.json")), TimeProvider.System)),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
                broadcastProfiles: profilesSection, platformRegistry: registry);

            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [],
                null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var ytControls = new YouTubeMultistreamPlatformControls(settings);
            var page = new MultistreamPageViewModel(settings, status, [ytControls]);

            page.OpenPlatform(StreamingPlatform.YouTube);
            Assert.Multiple(() =>
            {
                Assert.That(page.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
                Assert.That(page.HasPlatformSettings, Is.True);
                Assert.That(page.IsStep1Complete, Is.False);
                Assert.That(page.IsStep1Expanded, Is.True);
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Parameters));
            });

            page.OpenAutomationCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(page.IsAutomationSelected, Is.True);
                Assert.That(page.ProfilesBlocks.Select(b => b.Key), Is.EqualTo(new[] { "platforms/profiles" }));
                Assert.That(page.LifecycleAndBroadcastBlocks.Select(b => b.Key), Is.EquivalentTo(new[] { "stream/auto", "stream/lifecycle" }));
                Assert.That(page.ChannelGroupsBlocks.Select(b => b.Key), Is.EqualTo(new[] { "channels/groups" }));
                Assert.That(page.SelectedAutomationTab, Is.EqualTo(MultistreamAutomationTab.Profiles));
            });

            page.SelectAutomationTabCommand.Execute(MultistreamAutomationTab.Automation);
            Assert.That(page.IsAutomationTabSelected, Is.True);
            page.SelectAutomationTabCommand.Execute(MultistreamAutomationTab.Groups);
            Assert.That(page.IsGroupsTabSelected, Is.True);
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public void Two_youtube_channels_expose_active_preselected_and_role_summaries_and_switch_active()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-2ch-");
        try
        {
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube")]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"));
            var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"));
            ytAccounts.UpsertContext(PlatformAuthRole.Bot, "yt-bot", "alex_yt", new YouTubeAccountSettings { RefreshToken = "tok" });
            ytAccounts.UpsertContext(PlatformAuthRole.Broadcaster, "yt-host", "VissGames", new YouTubeAccountSettings { RefreshToken = "tok2" });

            Assert.That(catalog.Save(new()
            {
                Channels =
                [
                    new()
                    {
                        Platform = StreamingPlatform.YouTube,
                        ChannelId = "UC_1",
                        DisplayName = "Основной",
                        IsConfigured = true,
                        MonitoringEnabled = true,
                        BotAccountId = "alex_yt_id",
                        BotContextId = "yt-bot",
                    },
                    new()
                    {
                        Platform = StreamingPlatform.YouTube,
                        ChannelId = "UC_2",
                        DisplayName = "Запасной",
                        IsConfigured = true,
                        MonitoringEnabled = false,
                    },
                ],
                Platforms =
                [
                    new()
                    {
                        Platform = StreamingPlatform.YouTube,
                        ActiveChannelId = "UC_1",
                        SwitchMode = ChannelSwitchMode.PreselectedNext,
                        PreselectedNextChannelId = "UC_2",
                    },
                ],
            }), Is.True);

            var adapter = new MultiChannelFakeAdapter(StreamingPlatform.YouTube, ytAccounts);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], youtubeAccountsStore: ytAccounts, platformRegistry: registry);
            editor.SelectedPlatform = StreamingPlatform.YouTube;
            editor.Load();

            Assert.Multiple(() =>
            {
                Assert.That(editor.PlatformChannelsTitle, Is.EqualTo("Каналы YouTube"));
                Assert.That(editor.VisibleChannels, Has.Count.EqualTo(2));
                Assert.That(editor.VisibleChannels[0].IsActive, Is.True);
                Assert.That(editor.VisibleChannels[0].IsPreselectedNext, Is.False);
                Assert.That(editor.VisibleChannels[0].MonitoringSummaryText, Is.EqualTo("Фоновый мониторинг: вкл."));
                Assert.That(editor.VisibleChannels[1].IsActive, Is.False);
                Assert.That(editor.VisibleChannels[1].IsPreselectedNext, Is.True);
                Assert.That(editor.VisibleChannels[1].BotSummaryText, Is.EqualTo("Бот: не назначен"));
                Assert.That(editor.VisibleChannels[1].BroadcasterSummaryText, Is.EqualTo("Стример: –"));
                Assert.That(editor.VisibleChannels[1].MonitoringSummaryText, Is.EqualTo("Фоновый мониторинг: выкл."));
                Assert.That(editor.VisibleChannels[1].EditorHeading, Is.EqualTo("Настройка канала «Запасной»"));
                Assert.That(editor.VisibleChannels[1].AutomationSummary, Does.Contain("Запасной").And.Contain("Следующий"));
            });

            editor.VisibleChannels[1].DisplayName = "Архив";
            Assert.That(editor.VisibleChannels[1].EditorHeading, Is.EqualTo("Настройка канала «Архив»"));

            editor.VisibleChannels[1].MakeActiveCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(editor.VisibleChannels[0].IsActive, Is.False);
                Assert.That(editor.VisibleChannels[1].IsActive, Is.True);
                Assert.That(editor.VisibleChannels[1].IsPreselectedNext, Is.False);
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ShellViewModel_ConnectButtonTooltip_lists_enabled_platforms()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-tooltip-");
        try
        {
            var twitchEnabled = true;
            var ytEnabled = false;
            var registry = new ChatPlatformRegistry(
            [
                new FakePlatform(StreamingPlatform.Twitch, "Twitch", () => twitchEnabled),
                new FakePlatform(StreamingPlatform.YouTube, "YouTube", () => ytEnabled),
            ]);

            var gate = new SettingsWriteGate();
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            manager.SaveSettings(new AppSettings
            {
                Twitch = new TwitchSettings
                {
                    Enabled = true,
                    ClientId = "cid",
                    ClientSecret = "sec",
                    Channel = "streamer",
                },
            });
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "acc.json"));
            accounts.SaveAll(new TwitchAccountSettings
            {
                AccessToken = "bot",
                RefreshToken = "ref",
                StoredScopes = [.. TwitchScopes.BotRequired],
            }, new());
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new MemorySettings();
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
            var windows = DispatchProxy.Create<IMainWindowController, InertProxy>();
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var basic = new BasicSettingsSectionViewModel();
            using var settings = new SettingsPageViewModel(
                [new BasicSettingsSectionContributor(basic, new(), new(), manager, new StubTargetChannelProvider())],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, store, platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            using var tile = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher, ytSettings);
            using var overview = new DashboardViewModel([tile], new DashboardLayoutCoordinator(new DashboardLayoutStore(filePath: Path.Combine(data.FullName, "layout.json"))), TimeProvider.System);
            var history = new BroadcastHistoryStore(null, Path.Combine(data.FullName, "broadcasts.json"), Path.Combine(data.FullName, "sessions.json"));
            var boxArt = new GameBoxArtProvider(DispatchProxy.Create<IGameBoxArtCache, InertProxy>(), NullLogger<GameBoxArtProvider>.Instance);
            using var streamHistory = new StreamHistoryPageViewModel(history, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance), store, boxArt, bus);
            using var updateBanner = new UpdateBannerViewModel(
                DispatchProxy.Create<KeepShell.Updates.IUpdateCoordinator, InertProxy>(),
                DispatchProxy.Create<IUpdateEnvironment, InertProxy>(),
                dialogs,
                NullLogger<UpdateBannerViewModel>.Instance,
                dispatcher,
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                windows);
            using var onboardingBanner = new OnboardingBannerViewModel(new OnboardingChecklist(manager, accounts), bus);
            var debugBanner = new DebugBannerViewModel(new TargetChannelProvider(manager, new DebugChannelStore(filePath: Path.Combine(data.FullName, "dbg.json")), DebugChannelOverride.None));
            var preferences = new ShellPreferences(store) { LastPage = SectionKeys.Settings };

            using var shell = new ShellViewModel(
                new ModalHostViewModel(dispatcher), overview, settings, null!, streamHistory, null!, null!, null!, null!,
                preferences, connection, bus, NullLogger<ShellViewModel>.Instance, dialogs, updateBanner, onboardingBanner, debugBanner,
                null!, null!, store, dispatcher, DispatchProxy.Create<IUnsavedChangesPrompt, InertProxy>(), windows, registry);

            var changedProperties = new List<string?>();
            shell.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            Assert.That(shell.ConnectButtonTooltip, Is.EqualTo("Управление ботом на площадке: Twitch"));

            ytEnabled = true;
            await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Connected));
            Assert.Multiple(() =>
            {
                Assert.That(changedProperties, Does.Contain(nameof(ShellViewModel.ConnectButtonTooltip)));
                Assert.That(shell.ConnectButtonTooltip, Is.EqualTo("Управление ботом на всех включённых площадках: Twitch, YouTube"));
            });

            changedProperties.Clear();
            twitchEnabled = false;
            ytEnabled = false;
            basic.Channel = "other_channel";
            await settings.SaveCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(changedProperties, Does.Contain(nameof(ShellViewModel.ConnectButtonTooltip)));
                Assert.That(shell.ConnectButtonTooltip, Is.EqualTo("Нет включённых площадок для подключения"));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Disabled_third_platform_uses_configure_label_and_hides_unmanageable_role_summaries()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-3rd-");
        try
        {
            var vk = new FakePlatform(StreamingPlatform.VkLive, "VK Видео Live", () => false);
            var registry = new ChatPlatformRegistry([vk]);
            var gate = new SettingsWriteGate();
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            Assert.That(catalog.Save(new()
            {
                Channels = [new() { Platform = StreamingPlatform.VkLive, ChannelId = "vk-1", DisplayName = "Канал VK", IsConfigured = true }],
                Platforms = [new() { Platform = StreamingPlatform.VkLive, ActiveChannelId = "vk-1" }],
            }), Is.True);
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            using var settings = new SettingsPageViewModel([new ChannelsSettingsSectionContributor(editor, groups, catalog)],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance,
                DispatchProxy.Create<IDialogService, NoDialog>(), new MemorySettings(), platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, new ManualUiDispatcher(), new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json")));
            var page = new MultistreamPageViewModel(settings, status, []);

            page.OpenPlatform(StreamingPlatform.VkLive);

            var card = page.Cards.Single();
            var channel = editor.VisibleChannels.Single();
            Assert.Multiple(() =>
            {
                Assert.That(card.Status.IsDisabled, Is.True);
                Assert.That(card.Status.PlatformActionLabel, Is.EqualTo("Настроить"));
                Assert.That(page.HasPlatformSettings, Is.False);
                Assert.That(page.HasStep1, Is.False);
                Assert.That(page.ShowParametersButtonInHeader, Is.False);
                Assert.That(page.HeaderSetupActionLabel, Is.EqualTo("Открыть параметры"));
                Assert.That(channel.BotSummaryText, Is.Empty);
                Assert.That(channel.BotSummarySeverity, Is.EqualTo(StatusSeverity.None));
                Assert.That(channel.BroadcasterSummaryText, Is.Empty);
                Assert.That(channel.BroadcasterSummarySeverity, Is.EqualTo(StatusSeverity.None));
                Assert.That(card.AutomationSummary, Does.Contain("VK Видео Live").And.Contain("Канал VK"));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Toggling_youtube_enabled_marks_dirty_and_discard_reverts_it()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-dirty-");
        try
        {
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube", () => false)]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));

            using var settings = new SettingsPageViewModel(
                [
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
                platformRegistry: registry);

            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var ytControls = new YouTubeMultistreamPlatformControls(settings);
            var page = new MultistreamPageViewModel(settings, status, [ytControls]);

            var changedHeaderProperties = new List<string>();
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MultistreamPageViewModel.ShowParametersButtonInHeader)
                    or nameof(MultistreamPageViewModel.ShowBotAuthButtonInHeader)
                    or nameof(MultistreamPageViewModel.HeaderSetupActionLabel))
                {
                    changedHeaderProperties.Add(e.PropertyName);
                }
            };

            Assert.Multiple(() =>
            {
                Assert.That(ytVm.Enabled, Is.False);
                Assert.That(settings.HasChanges, Is.False);
                Assert.That(ytControls.SetupActionLabel, Is.EqualTo("Включить в параметрах"));
                Assert.That(page.HeaderSetupActionLabel, Is.EqualTo("Включить в параметрах"));
                Assert.That(page.ShowParametersButtonInHeader, Is.True);
                Assert.That(page.ShowBotAuthButtonInHeader, Is.False);
            });

            ytVm.Enabled = true;
            Assert.Multiple(() =>
            {
                Assert.That(settings.HasChanges, Is.True);
                Assert.That(page.HasUnsavedChanges, Is.True);
                Assert.That(page.ShowParametersButtonInHeader, Is.False);
                Assert.That(changedHeaderProperties, Does.Contain(nameof(MultistreamPageViewModel.ShowParametersButtonInHeader)));
            });

            changedHeaderProperties.Clear();
            page.SelectedCard!.Status.ApplyChat(enabled: true, blockedByDebug: false, BotLifecyclePhase.Idle, null, lacksBotAuthorization: true);
            Assert.Multiple(() =>
            {
                Assert.That(page.ShowBotAuthButtonInHeader, Is.True);
                Assert.That(changedHeaderProperties, Does.Contain(nameof(MultistreamPageViewModel.ShowBotAuthButtonInHeader)));
            });

            changedHeaderProperties.Clear();
            page.SelectedCard.Status.ApplyChat(enabled: true, blockedByDebug: false, BotLifecyclePhase.Idle, null, lacksBotAuthorization: false);
            Assert.Multiple(() =>
            {
                Assert.That(page.ShowBotAuthButtonInHeader, Is.False);
                Assert.That(changedHeaderProperties, Does.Contain(nameof(MultistreamPageViewModel.ShowBotAuthButtonInHeader)));
            });

            page.SelectedCard.Status.ApplyChat(enabled: false, blockedByDebug: false, BotLifecyclePhase.Idle, null, lacksBotAuthorization: false);
            changedHeaderProperties.Clear();
            page.DiscardUnsavedChangesCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(ytVm.Enabled, Is.False);
                Assert.That(settings.HasChanges, Is.False);
                Assert.That(page.HasUnsavedChanges, Is.False);
                Assert.That(page.ShowParametersButtonInHeader, Is.True);
                Assert.That(changedHeaderProperties, Does.Contain(nameof(MultistreamPageViewModel.ShowParametersButtonInHeader)));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [TestCase(false, 879, 1.0, true)]
    [TestCase(false, 880, 1.0, false)]
    [TestCase(true, 919, 1.0, true)]
    [TestCase(true, 920, 1.0, false)]
    [TestCase(false, 1024, 1.6, true)]
    [TestCase(true, 1471, 1.6, true)]
    [TestCase(true, 1472, 1.6, false)]
    [TestCase(true, 0, 1.0, true)]
    public void MultistreamPageView_compact_layout_hysteresis_scales_with_font(
        bool currentCompact, double width, double scale, bool expectedCompact)
    {
        Assert.That(
            PoproshaykaBot.Wpf.Views.MultistreamPageView.ResolveCompactLayout(currentCompact, width, scale),
            Is.EqualTo(expectedCompact));
    }

    [TestCase(false, 699, 1.0, true)]
    [TestCase(false, 700, 1.0, false)]
    [TestCase(true, 729, 1.0, true)]
    [TestCase(true, 730, 1.0, false)]
    [TestCase(false, 1119, 1.6, true)]
    [TestCase(true, 1167, 1.6, true)]
    [TestCase(true, 1168, 1.6, false)]
    public void ChannelCatalogSettingsSectionView_narrow_layout_hysteresis_scales_with_font(
        bool currentNarrow, double width, double scale, bool expectedNarrow)
    {
        Assert.That(
            PoproshaykaBot.Wpf.Views.Settings.ChannelCatalogSettingsSectionView.ResolveNarrowLayout(currentNarrow, width, scale),
            Is.EqualTo(expectedNarrow));
    }

    [TestCase(false, 639, 1.0, false)]
    [TestCase(false, 640, 1.0, true)]
    [TestCase(true, 600, 1.0, true)]
    [TestCase(true, 599, 1.0, false)]
    [TestCase(false, 1023, 1.6, false)]
    [TestCase(false, 1024, 1.6, true)]
    [TestCase(true, 959, 1.6, false)]
    [TestCase(true, 0, 1.0, true)]
    public void ChannelCatalogSettingsSectionView_two_column_cards_hysteresis_scales_with_font(
        bool currentTwoColumn, double width, double scale, bool expectedTwoColumn)
    {
        Assert.That(
            PoproshaykaBot.Wpf.Views.Settings.ChannelCatalogSettingsSectionView.ResolveTwoColumnCards(currentTwoColumn, width, scale),
            Is.EqualTo(expectedTwoColumn));
    }

    [Test]
    public async Task Vertical_123_route_collapses_ready_steps_explains_dependencies_and_prefills_account_on_add_channel()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-123-");
        try
        {
            var ytEnabled = false;
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube", () => ytEnabled)]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "accounts.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();

            ytAccountsStore.SaveAll(
                "secret",
                new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
                new YouTubeAccountSettings { ChannelId = "UC_MAIN", ChannelTitle = "OwnerYT", AccessToken = "tok2", RefreshToken = "ref2", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });

            var dialogSpy = new CapturingDialogService();
            var adapter = new MultiChannelFakeAdapter(StreamingPlatform.YouTube, ytAccountsStore,
                resolve: (raw, _, _) => Task.FromResult<ResolvedChannelIdentity?>(
                    new(new(StreamingPlatform.YouTube, "UC_MAIN"), raw, "@main", "https://youtube.com/@main")));
            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogSpy, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], accounts, ytAccountsStore, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            var platformAccounts = new PlatformAccountsSectionViewModel(
                catalog, accounts, ytAccountsStore,
                youtubeOAuth: DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                youtubeSettingsStore: ytSettingsStore,
                shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogService: dialogSpy,
                platformRegistry: registry);

            using var settings = new SettingsPageViewModel(
                [
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogSpy, new MemorySettings(),
                platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var ytControls = new YouTubeMultistreamPlatformControls(settings);
            var page = new MultistreamPageViewModel(settings, status, [ytControls], platformAccounts, ytSettingsStore, catalog);
            page.OnEnter();

            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep1Complete, Is.False);
                Assert.That(page.IsStep1Expanded, Is.True);
                Assert.That(page.IsStep2Complete, Is.False);
                Assert.That(page.IsStep2Expanded, Is.True);
                Assert.That(platformAccounts.CanAuthorize, Is.True, "Кнопки авторизации не должны скрываться молча при незаполненном Шаге 1");
                Assert.That(platformAccounts.Step1DependencyNotice, Does.Contain("Шаг 1 («Доступ площадки»)"));
                Assert.That(editor.StepDependencyNotice, Does.Contain("Шаг 1 («Доступ площадки»)"));
            });

            ytVm.Enabled = true;
            ytVm.ClientId = "yt-client";
            ytVm.ClientSecret = "secret";
            ytEnabled = true;
            Assert.That(page.IsStep1Complete, Is.False, "Несохранённые изменения Шага 1 не должны сворачивать его как готовый");
            Assert.That(platformAccounts.Step1DependencyNotice, Does.Contain("Шаг 1 («Доступ площадки»)"));

            await settings.SaveCommand.ExecuteAsync(null);
            page.OnEnter();

            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep1Complete, Is.True);
                Assert.That(page.IsStep1CollapsedSummaryVisible, Is.True);
                Assert.That(page.IsStep1Expanded, Is.False);
                Assert.That(platformAccounts.Step1DependencyNotice, Is.Null);
                Assert.That(page.IsStep2Complete, Is.True);
                Assert.That(page.IsStep2CollapsedSummaryVisible, Is.True);
                Assert.That(page.IsStep2Expanded, Is.False);
                Assert.That(editor.StepDependencyNotice, Is.Null);
            });

            page.ToggleStep1Command.Execute(null);
            page.ToggleStep2Command.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep1Expanded, Is.True);
                Assert.That(page.IsStep2Expanded, Is.True);
            });

            page.ToggleStep1Command.Execute(null);
            page.ToggleStep2Command.Execute(null);

            editor.NewChannelInput = "@main";
            await editor.AddChannelCommand.ExecuteAsync(null);
            var added = editor.SelectedChannel!;
            Assert.Multiple(() =>
            {
                Assert.That(added.ChannelId, Is.EqualTo("UC_MAIN"));
                Assert.That(added.Roles[0].AccountId, Is.EqualTo("UC_BOT"), "Роль бота должна автоподставляться из Шага 2");
                Assert.That(added.Roles[1].AccountId, Is.EqualTo("UC_MAIN"), "Роль стримера должна автоподставляться из Шага 2");
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Switching_unconfigured_to_no_step_platform_then_configured_notifies_green_step_summaries()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-b1-");
        try
        {
            var registry = new ChatPlatformRegistry(
            [
                new FakePlatform(StreamingPlatform.Twitch, "Twitch"),
                new FakePlatform(StreamingPlatform.VkLive, "VK Видео Live"),
                new FakePlatform(StreamingPlatform.YouTube, "YouTube"),
            ]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "accounts.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

            ytSettingsStore.Save(new() { Enabled = true, ClientId = "yt-client" });
            ytAccountsStore.SaveAll(
                "yt-secret",
                new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
                new());

            var oauth = new OAuthSettingsViewModel(
                DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
                accounts, dialogs, DispatchProxy.Create<IShellLauncher, InertProxy>(),
                DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
                dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            var platformAccounts = new PlatformAccountsSectionViewModel(
                catalog, accounts, ytAccountsStore,
                youtubeOAuth: DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                youtubeSettingsStore: ytSettingsStore,
                shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogService: dialogs,
                platformRegistry: registry);

            using var settings = new SettingsPageViewModel(
                [
                    new TwitchConnectionSettingsContributor(oauth, accounts, manager, NullLogger<TwitchConnectionSettingsContributor>.Instance),
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
                platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var page = new MultistreamPageViewModel(settings, status, [new YouTubeMultistreamPlatformControls(settings)], platformAccounts, ytSettingsStore, catalog);
            page.OnEnter();

            Assert.Multiple(() =>
            {
                Assert.That(page.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));
                Assert.That(page.IsStep1Complete, Is.False);
                Assert.That(page.IsStep2Complete, Is.False);
            });

            page.SelectPlatformCommand.Execute(page.Cards.Single(c => c.Status.Platform == StreamingPlatform.VkLive));
            Assert.Multiple(() =>
            {
                Assert.That(page.HasStep1, Is.False);
                Assert.That(page.HasStep2, Is.False);
                Assert.That(page.IsStep1CollapsedSummaryVisible, Is.False);
                Assert.That(page.IsStep2CollapsedSummaryVisible, Is.False);
            });

            var notified = new List<string?>();
            page.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

            page.SelectPlatformCommand.Execute(page.Cards.Single(c => c.Status.Platform == StreamingPlatform.YouTube));

            Assert.Multiple(() =>
            {
                Assert.That(page.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
                Assert.That(page.IsStep1CollapsedSummaryVisible, Is.True);
                Assert.That(page.IsStep2CollapsedSummaryVisible, Is.True);
                Assert.That(notified, Does.Contain(nameof(MultistreamPageViewModel.IsStep1CollapsedSummaryVisible)));
                Assert.That(notified, Does.Contain(nameof(MultistreamPageViewModel.IsStep2CollapsedSummaryVisible)));
                Assert.That(notified, Does.Contain(nameof(MultistreamPageViewModel.IsStep1BodyVisible)));
                Assert.That(notified, Does.Contain(nameof(MultistreamPageViewModel.IsStep2BodyVisible)));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Quota_budget_error_blocks_step1_and_exposes_reason_in_step2_and_step3()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-b2-");
        try
        {
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube")]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "accounts.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

            ytSettingsStore.Save(new() { Enabled = true, ClientId = "yt-client" });
            ytAccountsStore.SaveAll("yt-secret", new(), new());

            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            var platformAccounts = new PlatformAccountsSectionViewModel(
                catalog, accounts, ytAccountsStore,
                youtubeOAuth: DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                youtubeSettingsStore: ytSettingsStore,
                shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogService: dialogs,
                platformRegistry: registry);

            using var settings = new SettingsPageViewModel(
                [
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
                platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var page = new MultistreamPageViewModel(settings, status, [new YouTubeMultistreamPlatformControls(settings)], platformAccounts, ytSettingsStore, catalog);
            page.OnEnter();

            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep1Complete, Is.True);
                Assert.That(platformAccounts.Step1DependencyNotice, Is.Null);
            });

            ytVm.DailyQuotaBudgetText = "0";

            Assert.Multiple(() =>
            {
                Assert.That(ytVm.HasQuotaBudgetError, Is.True);
                Assert.That(page.IsStep1Complete, Is.False);
                Assert.That(page.IsStep2Complete, Is.False);
                Assert.That(platformAccounts.HasStep1DependencyNotice, Is.True);
                Assert.That(platformAccounts.Step1DependencyNotice, Does.Contain("бюджет квоты"));
                Assert.That(editor.StepDependencyNotice, Does.Contain("бюджет квоты"));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task OpenPlatform_revealing_incomplete_step2_auto_collapses_after_sign_in()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-b3-");
        try
        {
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube")]);
            var gate = new SettingsWriteGate();
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "accounts.json"), gate: gate);
            var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json"), gate: gate);
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"), gate: gate);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

            ytSettingsStore.Save(new() { Enabled = true, ClientId = "yt-client" });
            ytAccountsStore.SaveAll("yt-secret", new(), new());

            var ytVm = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            var login = new StubPlatformAccountLogin(
                StreamingPlatform.YouTube,
                () =>
                {
                    ytAccountsStore.SaveAll(
                        "yt-secret",
                        new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
                        new());
                    return new(
                        "Вход выполнен",
                        StatusSeverity.Success,
                        [new AuthContextSummary(AuthContextIds.Default, PlatformAuthRole.Bot, "BotYT", "UC_BOT", "BotYT", true, true)]);
                });
            var platformAccounts = new PlatformAccountsSectionViewModel(
                catalog, accounts, ytAccountsStore,
                youtubeSettingsStore: ytSettingsStore,
                shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogService: dialogs,
                accountLogins: [login],
                platformRegistry: registry);

            using var settings = new SettingsPageViewModel(
                [
                    new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                    new ChannelsSettingsSectionContributor(editor, groups, catalog),
                ],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
                platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, dispatcher, ytSettingsStore);
            var page = new MultistreamPageViewModel(settings, status, [new YouTubeMultistreamPlatformControls(settings)], platformAccounts, ytSettingsStore, catalog);
            page.OnEnter();

            page.OpenPlatform(StreamingPlatform.YouTube);
            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep1Complete, Is.True);
                Assert.That(page.IsStep2Complete, Is.False);
                Assert.That(page.IsStep2Expanded, Is.True);
                Assert.That(page.IsStep2UserExpanded, Is.False);
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Accounts));
            });

            await platformAccounts.AuthorizeBotCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(page.IsStep2Complete, Is.True);
                Assert.That(page.IsStep2Expanded, Is.False);
                Assert.That(page.IsStep2CollapsedSummaryVisible, Is.True);
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Add_youtube_channel_without_matching_broadcaster_leaves_broadcaster_unassigned()
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-b4-");
        try
        {
            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.YouTube, "YouTube")]);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"));
            var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(data.FullName, "youtube-accounts.json"));
            ytAccountsStore.SaveAll(
                "secret",
                new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
                new YouTubeAccountSettings { ChannelId = "UC_MAIN", ChannelTitle = "OwnerYT", AccessToken = "tok2", RefreshToken = "ref2", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });

            var adapter = new MultiChannelFakeAdapter(
                StreamingPlatform.YouTube,
                ytAccountsStore,
                resolve: (raw, _, _) => Task.FromResult<ResolvedChannelIdentity?>(
                    new(new(StreamingPlatform.YouTube, "UC_OTHER"), raw, "@other", "https://youtube.com/@other")));
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], youtubeAccountsStore: ytAccountsStore, platformRegistry: registry)
            {
                SelectedPlatform = StreamingPlatform.YouTube,
            };
            editor.Load();

            editor.NewChannelInput = "@other";
            await editor.AddChannelCommand.ExecuteAsync(null);

            var added = editor.SelectedChannel!;
            Assert.Multiple(() =>
            {
                Assert.That(added.ChannelId, Is.EqualTo("UC_OTHER"));
                Assert.That(added.Roles[0].AccountId, Is.EqualTo("UC_BOT"), "Роль бота должна безопасно автоподставляться");
                Assert.That(added.Roles[1].AccountId, Is.Null.Or.Empty, "Чужой аккаунт стримера не должен подставляться в другой канал");
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    public sealed class CapturingDialogService : IDialogService
    {
        public IDialogViewModel? LastDialog { get; private set; }

        public Func<IDialogViewModel, Task<bool>>? OnShowAsync { get; set; }

        public Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            LastDialog = viewModel;
            return OnShowAsync is not null ? OnShowAsync(viewModel) : Task.FromResult(false);
        }

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => ShowAsync(viewModel);
        public bool Confirm(string title, string message, bool defaultYes = false) => defaultYes;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => defaultYes;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    public class NoDialog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }

    private sealed class FakePlatform(StreamingPlatform id, string displayName, Func<bool>? isEnabled = null) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled?.Invoke() ?? true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus { get; } = new FakeStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

    private sealed class MultiChannelFakeAdapter(
        StreamingPlatform platform,
        YouTubeAccountsStore accounts,
        Func<string, string?, CancellationToken, Task<ResolvedChannelIdentity?>>? resolve = null) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;
        public IReadOnlyList<PlatformAccount> ListAccounts() => accounts.ListAccounts();
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken) =>
            resolve is not null ? resolve(rawInput, preferredContextId, cancellationToken) : Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken) =>
            Task.FromResult(new ChannelAccessVerification(true, "id"));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken) =>
            Task.FromResult(new ChannelLiveProbeResult(false, false, null));
    }

    private sealed class StubTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = new("channel", "channel", false, true);
        public void BeginSession() { }
        public void EndSession() { }
    }

    private sealed class FakeStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => throw new NotSupportedException();
    }

    private sealed class StubPlatformAccountLogin(
        StreamingPlatform platform,
        Func<PlatformAccountLoginResult> authorize) : IPlatformAccountLogin
    {
        public StreamingPlatform Platform => platform;
        public bool CanAuthorize => true;
        public bool SupportsEmbeddedBrowser => false;
        public bool SupportsWebChatSignIn => false;
        public PlatformOAuthMethodDescriptor OAuthMethod { get; } = new("Встроенное окно", "Войти через окно", false, false);
        public Task<PlatformAccountLoginResult> AuthorizeAsync(PlatformAccountLoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(authorize());
    }
}
