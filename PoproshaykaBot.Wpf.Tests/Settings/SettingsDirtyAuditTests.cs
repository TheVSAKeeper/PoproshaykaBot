using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using UpdateCandidate = KeepShell.Updates.UpdateCandidate;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class SettingsDirtyAuditTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-dirty-audit-" + Guid.NewGuid().ToString("N"));
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

    public sealed record EditableSectionCase(string Name, Func<AuditHarness, Task> Edit, Action<AuditHarness> AssertPersisted)
    {
        public EditableSectionCase(string name, Action<AuditHarness> edit, Action<AuditHarness> assertPersisted)
            : this(name, h =>
            {
                edit(h);
                return Task.CompletedTask;
            }, assertPersisted)
        {
        }
    }

    public sealed record NonDirtyCase(string Name, Func<AuditHarness, Task> Act);

    private static IEnumerable<EditableSectionCase> EditableSectionCases()
    {
        yield return new(
            "Basic: ChatDisplayAccount",
            h => h.Basic.ChatDisplayAccount = PlatformAuthRole.Broadcaster,
            h => Assert.That(h.SettingsManager.Current.Twitch.ChatDisplayAccount, Is.EqualTo(PlatformAuthRole.Broadcaster)));

        yield return new(
            "Basic: FetchRecentMessages",
            h => h.Basic.FetchRecentMessages = true,
            h => Assert.That(h.SettingsManager.Current.Twitch.FetchRecentMessages, Is.True));

        yield return new(
            "RateLimiting: MessagesAllowedInPeriod",
            h => h.RateLimiting.MessagesAllowedInPeriod = 42,
            h => Assert.That(h.SettingsManager.Current.Twitch.MessagesAllowedInPeriod, Is.EqualTo(42)));

        yield return new(
            "RateLimiting: ThrottlingPeriodSeconds",
            h => h.RateLimiting.ThrottlingPeriodSeconds = 60,
            h => Assert.That(h.SettingsManager.Current.Twitch.ThrottlingPeriodSeconds, Is.EqualTo(60)));

        yield return new(
            "Twitch OAuth: ClientId and RedirectUri",
            h =>
            {
                h.OAuth.ClientId = "twitch-client-new";
                h.OAuth.RedirectUri = "http://localhost:9090";
            },
            h =>
            {
                Assert.That(h.SettingsManager.Current.Twitch.ClientId, Is.EqualTo("twitch-client-new"));
                Assert.That(h.SettingsManager.Current.Twitch.RedirectUri, Is.EqualTo("http://localhost:9090"));
                Assert.That(h.SettingsManager.Current.Twitch.HttpServerPort, Is.EqualTo(9090));
            });

        yield return new(
            "YouTube: Enabled, ProjectId and DailyQuotaBudgetText",
            h =>
            {
                h.YouTube.Enabled = true;
                h.YouTube.ProjectId = "yt-proj-2";
                h.YouTube.DailyQuotaBudgetText = "15000";
            },
            h =>
            {
                var saved = h.YouTubeSettingsStore.Load();
                Assert.That(saved.Enabled, Is.True);
                Assert.That(saved.ProjectId, Is.EqualTo("yt-proj-2"));
                Assert.That(saved.DailyQuotaBudget, Is.EqualTo(15000));
            });

        yield return new(
            "Chat: ShowHistoryInApp, ShowHistoryInObs, HistoryMaxItems",
            h =>
            {
                h.Chat.ShowHistoryInApp = false;
                h.Chat.ShowHistoryInObs = true;
                h.Chat.HistoryMaxItems = 500;
            },
            h =>
            {
                var saved = h.ChatSettingsStore.Load();
                Assert.That(saved.ShowHistoryInApp, Is.False);
                Assert.That(saved.ShowHistoryInObs, Is.True);
                Assert.That(saved.HistoryMaxItems, Is.EqualTo(500));
            });

        yield return new(
            "ObsIntegration: Host, Port, Enabled",
            h =>
            {
                h.ObsIntegration.Enabled = true;
                h.ObsIntegration.Host = "192.168.1.50";
                h.ObsIntegration.Port = 4466;
            },
            h =>
            {
                var saved = h.ObsIntegrationStore.Load();
                Assert.That(saved.Enabled, Is.True);
                Assert.That(saved.Host, Is.EqualTo("192.168.1.50"));
                Assert.That(saved.Port, Is.EqualTo(4466));
            });

        yield return new(
            "ObsChat: FontSize, BackgroundColor, ShowTimestamp",
            h =>
            {
                h.ObsChat.FontSize = 22;
                h.ObsChat.BackgroundColor = Color.FromArgb(120, 10, 20, 30);
                h.ObsChat.ShowTimestamp = true;
            },
            h =>
            {
                var saved = h.ObsChatStore.Load();
                Assert.That(saved.FontSize, Is.EqualTo(22));
                Assert.That(saved.BackgroundColor.ToArgb(), Is.EqualTo(Color.FromArgb(120, 10, 20, 30).ToArgb()));
                Assert.That(saved.ShowTimestamp, Is.True);
            });

        yield return new(
            "Stream BotLifecycle: Platform item flags",
            h =>
            {
                h.BotLifecycle.Platforms[0].AutoConnectOnStreamOnline = true;
                h.BotLifecycle.Platforms[0].AutoDisconnectOnStreamOffline = true;
            },
            h =>
            {
                var saved = h.PlatformLifecycleStore.Load();
                Assert.That(saved.Platforms[StreamingPlatform.Twitch].AutoConnectOnStreamOnline, Is.True);
                Assert.That(saved.Platforms[StreamingPlatform.Twitch].AutoDisconnectOnStreamOffline, Is.True);
            });

        yield return new(
            "Stream Polls: HistoryMaxItems and KillSwitchToday",
            h =>
            {
                h.Polls.HistoryMaxItems = 90;
                h.Polls.KillSwitchToday = true;
            },
            h =>
            {
                var saved = h.PollsStore.Load();
                Assert.That(saved.HistoryMaxItems, Is.EqualTo(90));
                Assert.That(saved.AutoTriggerKillSwitchDateUtc, Is.Not.Null);
            });

        yield return new(
            "Ranks: AddCommand and Entry edit",
            h =>
            {
                h.Ranks.AddCommand.Execute(null);
                h.Ranks.Entries[0].Name = "ЛЕГЕНДА";
            },
            h => Assert.That(h.SettingsManager.Current.Ranks.Ranks.Any(r => r.Name == "ЛЕГЕНДА"), Is.True));

        yield return new(
            "Channels Catalog: SwitchMode",
            h => h.ChannelCatalog.SelectedSwitchModeOption = h.ChannelCatalog.SwitchModeOptions.First(o => o.Mode == ChannelSwitchMode.AutoDiscover),
            h => Assert.That(h.CatalogStore.Load().Platforms.Single(p => p.Platform == StreamingPlatform.Twitch).SwitchMode, Is.EqualTo(ChannelSwitchMode.AutoDiscover)));

        yield return new(
            "Channels Groups: AddGroup and Rename",
            h =>
            {
                h.ChannelGroups.NewGroupName = "Основная группа";
                h.ChannelGroups.AddGroupCommand.Execute(null);
                h.ChannelGroups.SelectedGroup!.Name = "Переименованная";
            },
            h => Assert.That(h.CatalogStore.Load().Groups.Select(g => g.Name), Is.EqualTo(new[] { "Переименованная" })));

        yield return new(
            "Dashboard: SetMaxWidth",
            h =>
            {
                var typeId = h.DashboardLayout.BuildLayout().Tiles[0].TypeId;
                h.DashboardLayout.SetMaxWidth(typeId, 500);
            },
            h => Assert.That(h.LayoutStore.LoadDashboard()!.Tiles[0].MaxWidth, Is.EqualTo(500)));

        yield return new(
            "Update: AutomaticCheckEnabled, CheckIntervalHours",
            h =>
            {
                h.Update.AutomaticCheckEnabled = false;
                h.Update.CheckIntervalHours = 12;
            },
            h =>
            {
                var saved = h.UpdateStore.Load();
                Assert.That(saved.AutoCheckEnabled, Is.False);
                Assert.That(saved.CheckIntervalHours, Is.EqualTo(12));
            });

        yield return new(
            "Update: RepositoryOverride",
            h => h.Update.RepositoryOverride = "custom/repo",
            h => Assert.That(h.UpdateStore.Load().RepositoryOverride, Is.EqualTo("custom/repo")));

        yield return new(
            "DebugChannel: IsEnabled, Channel, AllowSending",
            h =>
            {
                h.DebugChannel.IsEnabled = true;
                h.DebugChannel.Channel = "test_streamer";
                h.DebugChannel.AllowSending = true;
            },
            h =>
            {
                var saved = h.DebugChannelStore.Load();
                Assert.That(saved.IsEnabled, Is.True);
                Assert.That(saved.Channel, Is.EqualTo("test_streamer"));
                Assert.That(saved.AllowSending, Is.True);
            });
    }

    private static IEnumerable<NonDirtyCase> NonDirtyCases()
    {
        yield return new("ObsIntegration: TestConnection and LoadScenes auto-populate", async h =>
        {
            await h.ObsIntegration.TestConnectionCommand.ExecuteAsync(null);
            await h.ObsIntegration.LoadScenesCommand.ExecuteAsync(null);
        });

        yield return new("ObsIntegration: StatusText and IsBusy", h =>
        {
            h.ObsIntegration.StatusText = "● Проверка";
            h.ObsIntegration.StatusSeverity = StatusSeverity.Info;
            h.ObsIntegration.OverlayUrl = "http://localhost:9999/chat";
            return Task.CompletedTask;
        });

        yield return new("Update: CheckNowCommand sets IsBusy and StatusText", h => h.Update.CheckNowCommand.ExecuteAsync(null));

        yield return new("YouTube: QuotaStatus RefreshCommand", h =>
        {
            h.YouTube.QuotaStatus.RefreshCommand.Execute(null);
            return Task.CompletedTask;
        });

        yield return new("Dashboard: Preview Reference and Scale", h =>
        {
            h.DashboardLayout.Scale = 1.5;
            h.DashboardLayout.Reference = h.DashboardLayout.References[1];
            return Task.CompletedTask;
        });

        yield return new("DebugChannel: Recent Reload", h =>
        {
            h.DebugChannel.Recent.Reload();
            return Task.CompletedTask;
        });

        yield return new("Draftless: MCP and Appearance preferences", h =>
        {
            h.Mcp.Preferences.Enabled = !h.Mcp.Preferences.Enabled;
            h.Appearance.Shell.ShowPageHeader = !h.Appearance.Shell.ShowPageHeader;
            return Task.CompletedTask;
        });
    }

    [TestCaseSource(nameof(EditableSectionCases))]
    public async Task Editing_section_marks_dirty_and_saves_to_store(EditableSectionCase testCase)
    {
        using var harness = new AuditHarness(_root);
        Assert.That(harness.Page.HasUnsavedChanges, Is.False);

        var unsavedNotifications = 0;
        harness.Page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsPageViewModel.HasUnsavedChanges))
            {
                unsavedNotifications++;
            }
        };

        await testCase.Edit(harness);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.HasChanges, Is.True, testCase.Name);
            Assert.That(harness.Page.HasUnsavedChanges, Is.True, testCase.Name);
            Assert.That(harness.Page.SaveCommand.CanExecute(null), Is.True, testCase.Name);
            Assert.That(unsavedNotifications, Is.GreaterThan(0), testCase.Name);
        });

        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.HasChanges, Is.False, testCase.Name);
            Assert.That(harness.Page.HasUnsavedChanges, Is.False, testCase.Name);
        });

        testCase.AssertPersisted(harness);
    }

    [TestCaseSource(nameof(EditableSectionCases))]
    public async Task Editing_section_and_reverting_clears_dirty_state(EditableSectionCase testCase)
    {
        using var harness = new AuditHarness(_root);

        await testCase.Edit(harness);
        Assert.That(harness.Page.HasUnsavedChanges, Is.True, testCase.Name);

        harness.Page.RevertCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.HasChanges, Is.False, testCase.Name);
            Assert.That(harness.Page.HasUnsavedChanges, Is.False, testCase.Name);
        });
    }

    [TestCaseSource(nameof(NonDirtyCases))]
    public async Task Non_dirty_properties_and_auto_population_do_not_mark_dirty(NonDirtyCase testCase)
    {
        using var harness = new AuditHarness(_root);

        await testCase.Act(harness);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.Dirty, Is.False, testCase.Name);
            Assert.That(harness.Page.HasChanges, Is.False, testCase.Name);
            Assert.That(harness.Page.HasUnsavedChanges, Is.False, testCase.Name);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Obs_sources_mark_dirty_before_and_after_obs_auto_population_and_persist_to_store(bool useDashboardSources)
    {
        using var harness = new AuditHarness(_root);
        harness.ObsIntegrationStore.Save(new ObsIntegrationSettings
        {
            DashboardSourceNames = ["Mic/Aux"],
            ChatRefreshSources = ["ChatOverlay"],
        });
        harness.Page.OnEnter();

        var initialCollection = useDashboardSources
            ? harness.ObsIntegration.DashboardSources
            : harness.ObsIntegration.ChatRefreshSources;

        Assert.That(harness.Page.HasUnsavedChanges, Is.False);
        initialCollection[0].IsChecked = false;
        Assert.That(harness.Page.HasUnsavedChanges, Is.True);

        harness.Page.RevertCommand.Execute(null);
        var revertedCollection = useDashboardSources
            ? harness.ObsIntegration.DashboardSources
            : harness.ObsIntegration.ChatRefreshSources;
        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.HasUnsavedChanges, Is.False);
            Assert.That(revertedCollection[0].IsChecked, Is.True, "Отмена должна вернуть исходное значение чекбокса");
        });

        var oldItem = revertedCollection[0];
        await harness.ObsIntegration.LoadScenesCommand.ExecuteAsync(null);
        Assert.That(harness.Page.HasUnsavedChanges, Is.False, "Автозаполнение из OBS не поднимает Dirty");

        oldItem.IsChecked = false;
        Assert.That(harness.Page.HasUnsavedChanges, Is.False, "Удалённый при пересоздании элемент отписан и не даёт ложный Dirty");

        var currentCollection = useDashboardSources
            ? harness.ObsIntegration.DashboardSources
            : harness.ObsIntegration.ChatRefreshSources;
        var addedByObs = currentCollection.First(s => !s.IsChecked);
        addedByObs.IsChecked = true;

        Assert.That(harness.Page.HasUnsavedChanges, Is.True);

        await harness.Page.SaveCommand.ExecuteAsync(null);
        var freshStore = new ObsIntegrationStore(
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            null,
            Path.Combine(_root, "obs-integration.json"));
        var saved = freshStore.Load();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Page.HasUnsavedChanges, Is.False);
            var stored = useDashboardSources ? saved.GetDashboardSourceNames() : saved.GetChatRefreshSourceNames();
            Assert.That(stored, Does.Contain(addedByObs.Name));
        });
    }

    [Test]
    public void TrackCollectionItems_handles_duplicate_references_and_value_equal_instances_without_leaks()
    {
        var collection = new ObservableCollection<ValueEqualItem>();
        var first = new ValueEqualItem("same");
        var second = new ValueEqualItem("same");
        collection.Add(first);
        collection.Add(first);
        collection.Add(second);

        var notifications = 0;
        using (var subscription = new CompositeSettingsSubscription())
        {
            subscription.TrackCollectionItems(collection, (_, _) => notifications++);

            first.Flag = true;
            Assert.That(notifications, Is.EqualTo(1), "Повторная ссылка в коллекции не должна подписываться дважды");

            collection.RemoveAt(2);
            second.Flag = true;
            Assert.That(notifications, Is.EqualTo(1), "Удалённый экземпляр с равным Equals должен быть отписан");

            first.Flag = false;
            Assert.That(notifications, Is.EqualTo(2), "Оставшийся экземпляр с тем же Equals сохраняет подписку");
        }

        first.Flag = true;
        second.Flag = false;
        Assert.That(notifications, Is.EqualTo(2), "После Dispose ни один экземпляр не должен слать события");
    }

    private sealed class ValueEqualItem(string id) : INotifyPropertyChanged
    {
        private bool _flag;

        public string Id { get; } = id;

        public bool Flag
        {
            get => _flag;
            set
            {
                _flag = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override bool Equals(object? obj) => obj is ValueEqualItem other && Id == other.Id;

        public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    }

    [Test]
    public async Task Closed_write_gate_on_obs_or_update_reports_not_written_notice()
    {
        using var harness = new AuditHarness(_root);
        await harness.CloseGateForAsync("obs-integration.json", "update.json");

        harness.ObsIntegration.Host = "127.0.0.2";
        harness.Update.CheckIntervalHours = 24;

        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Dialogs.Infos, Has.Count.EqualTo(1));
            Assert.That(harness.Dialogs.Infos[0], Does.Contain("не записаны"));
        });
    }

    [Test]
    public void Composite_subscription_tracks_add_remove_replace_reset_and_unsubscribes_on_dispose()
    {
        var collection = new ObservableCollection<ObsSourceItemViewModel>
        {
            new("First", false),
            new("Second", false),
        };
        var events = 0;
        var subscription = new CompositeSettingsSubscription();
        subscription.TrackCollectionItems(collection, (_, _) => events++);

        var first = collection[0];
        first.IsChecked = true;
        Assert.That(events, Is.EqualTo(1));

        var added = new ObsSourceItemViewModel("Third", false);
        collection.Add(added);
        added.IsChecked = true;
        Assert.That(events, Is.EqualTo(2));

        collection.Remove(first);
        first.IsChecked = false;
        Assert.That(events, Is.EqualTo(2));

        var replacement = new ObsSourceItemViewModel("Replacement", false);
        collection[0] = replacement;
        replacement.IsChecked = true;
        Assert.That(events, Is.EqualTo(3));

        collection.Clear();
        added.IsChecked = false;
        replacement.IsChecked = false;
        Assert.That(events, Is.EqualTo(3));

        var afterClear = new ObsSourceItemViewModel("AfterClear", false);
        collection.Add(afterClear);
        afterClear.IsChecked = true;
        Assert.That(events, Is.EqualTo(4));

        subscription.Dispose();
        afterClear.IsChecked = false;
        Assert.That(events, Is.EqualTo(4));
    }

    public sealed class AuditHarness : IDisposable
    {
        private readonly SettingsWriteGate _gate = new();

        public AuditHarness(string root)
        {
            var settingsManager = SettingsManager = new(NullLogger<SettingsManager>.Instance, Path.Combine(root, "settings.json"), _gate);
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var accountsStore = new AccountsStore(null, Path.Combine(root, "accounts.json"), _gate);
            ObsChatStore = new(bus, null, Path.Combine(root, "obs-chat.json"), _gate);
            ObsIntegrationStore = new(bus, null, Path.Combine(root, "obs-integration.json"), _gate);
            ChatSettingsStore = new(settingsManager, NullLogger<ChatSettingsStore>.Instance, Path.Combine(root, "chat.json"), _gate);
            UpdateStore = new(null, Path.Combine(root, "update.json"), _gate);
            DebugChannelStore = new(null, Path.Combine(root, "debug-channel.json"));
            PollsStore = new(null, Path.Combine(root, "polls.json"), _gate);
            LayoutStore = new(null, Path.Combine(root, "dashboard-layout.json"), _gate);
            YouTubeSettingsStore = new(null, Path.Combine(root, "youtube.json"), _gate);
            var youTubeAccountsStore = new YouTubeAccountsStore(null, Path.Combine(root, "youtube-accounts.json"), _gate);
            PlatformLifecycleStore = new(settingsManager, NullLogger<PlatformLifecycleSettingsStore>.Instance, Path.Combine(root, "platform-lifecycle.json"), _gate);
            CatalogStore = new(null, Path.Combine(root, "channels.json"), _gate);
            CatalogStore.Save(new ChannelsSettings
            {
                Channels =
                [
                    new ChannelCatalogEntry
                    {
                        Platform = StreamingPlatform.Twitch,
                        ChannelId = "1001",
                        DisplayName = "MainChannel",
                        IsConfigured = true,
                    },
                ],
                Platforms =
                [
                    new()
                    {
                        Platform = StreamingPlatform.Twitch,
                        ActiveChannelId = "1001",
                        SwitchMode = ChannelSwitchMode.Manual,
                    },
                ],
            });

            var uiSettings = new MemorySettings();
            var mcpOptions = new McpServerOptions
            {
                ServerName = "test",
                AppName = "test",
                AppVersion = "1.0",
                ShotsDirectory = Path.Combine(root, "shots"),
            };

            var registry = new ChatPlatformRegistry([new FakePlatform(StreamingPlatform.Twitch, "Twitch")]);
            var mcpPreferences = new McpPreferences(uiSettings, mcpOptions);
            var kestrel = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var obsClient = new FakeObsWebSocketClient();
            var obsService = new ObsIntegrationService(obsClient, settingsManager, NullLogger<ObsIntegrationService>.Instance);

            Basic = new();
            RateLimiting = new();
            var messaging = new BotMessagingNavigation();
            OAuth = new(
                DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
                accountsStore,
                Dialogs,
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
                new ManualUiDispatcher(),
                TimeProvider.System,
                NullLoggerFactory.Instance);
            YouTube = new(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                Dialogs,
                new ManualUiDispatcher(),
                TimeProvider.System,
                NullLoggerFactory.Instance);
            Chat = new();
            ObsIntegration = new(obsService, settingsManager, NullLogger<ObsIntegrationSectionViewModel>.Instance, Dialogs, DispatchProxy.Create<IClipboardService, InertProxy>());
            ObsChat = new(ObsChatStore, settingsManager, Dialogs, DispatchProxy.Create<IShellLauncher, InertProxy>());
            BotLifecycle = new(registry);
            Polls = new(PollsStore, TimeProvider.System);
            Ranks = new();
            ChannelCatalog = new(CatalogStore, [new FakeChannelAdapter(StreamingPlatform.Twitch)], platformRegistry: registry);
            ChannelGroups = new(CatalogStore, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance), platformRegistry: registry);
            DashboardLayout = new(Array.Empty<DashboardTileViewModel>(), uiSettings, LayoutStore);
            var updateEnv = new KeepShell.Updates.UpdateEnvironment(KeepShell.Updates.UpdateKind.None, false, new(1, 0), 10, "Bot.exe", "x64");
            Update = new(
                new UpdateStoreSettingsAdapter(UpdateStore),
                new FakeUpdateCoordinator(),
                PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateOptions("owner/repo"),
                updateEnv,
                Dialogs,
                new ManualUiDispatcher(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                DispatchProxy.Create<IMainWindowController, InertProxy>());
            DebugChannel = new(DebugChannelOverride.None, DebugChannelStore, new ChannelLiveStatusReader(DispatchProxy.Create<IHttpClientFactory, InertProxy>(), settingsManager, TimeProvider.System, NullLogger<ChannelLiveStatusReader>.Instance), TimeProvider.System, NullLogger<DebugChannelSectionViewModel>.Instance);
            Mcp = new(mcpPreferences, new(mcpPreferences, null!, null!, mcpOptions, new EmptyServiceProvider()), mcpOptions, Dialogs, DispatchProxy.Create<IClipboardService, InertProxy>());
            Appearance = new(new(uiSettings), new(uiSettings));
            var misc = new MiscSettingsSectionViewModel(settingsManager, null!, Dialogs, null!, null!, null!);

            ISettingsSectionContributor[] contributors =
            [
                new BasicSettingsSectionContributor(Basic, RateLimiting, messaging, settingsManager, new FakeTargetChannelProvider()),
                new TwitchConnectionSettingsContributor(OAuth, accountsStore, settingsManager, NullLogger<TwitchConnectionSettingsContributor>.Instance, CatalogStore),
                new YouTubeConnectionSettingsContributor(YouTube, YouTubeSettingsStore, youTubeAccountsStore),
                new ChatSettingsSectionContributor(Chat, ChatSettingsStore),
                new ObsSettingsSectionContributor(ObsIntegration, ObsChat, new(kestrel, Dialogs, null!), ObsIntegrationStore, ObsChatStore, Dialogs),
                new StreamSettingsSectionContributor(messaging, BotLifecycle, Polls, PlatformLifecycleStore),
                new RanksSettingsSectionContributor(Ranks),
                new ChannelsSettingsSectionContributor(ChannelCatalog, ChannelGroups, CatalogStore),
                new DashboardSettingsSectionContributor(DashboardLayout, new DashboardLayoutCoordinator(LayoutStore)),
                new UpdateSettingsSectionContributor(Update),
                new DebugChannelSettingsSectionContributor(DebugChannel, DebugChannelStore),
                new McpSettingsSectionContributor(Mcp),
                new AppearanceSettingsSectionContributor(Appearance),
                new MiscSettingsSectionContributor(misc),
            ];

            Page = new(
                contributors,
                settingsManager,
                _gate,
                kestrel,
                bus,
                NullLogger<SettingsPageViewModel>.Instance,
                Dialogs,
                uiSettings,
                platformRegistry: registry);
        }

        public SettingsPageViewModel Page { get; }
        public SettingsManager SettingsManager { get; }
        public ObsChatStore ObsChatStore { get; }
        public ObsIntegrationStore ObsIntegrationStore { get; }
        public ChatSettingsStore ChatSettingsStore { get; }
        public UpdateStore UpdateStore { get; }
        public DebugChannelStore DebugChannelStore { get; }
        public PollsStore PollsStore { get; }
        public DashboardLayoutStore LayoutStore { get; }
        public YouTubeSettingsStore YouTubeSettingsStore { get; }
        public PlatformLifecycleSettingsStore PlatformLifecycleStore { get; }
        public ChannelCatalogStore CatalogStore { get; }
        public FakeDialogService Dialogs { get; } = new();

        public BasicSettingsSectionViewModel Basic { get; }
        public RateLimitingSettingsViewModel RateLimiting { get; }
        public OAuthSettingsViewModel OAuth { get; }
        public YouTubeSettingsSectionViewModel YouTube { get; }
        public ChatSettingsSectionViewModel Chat { get; }
        public ObsIntegrationSectionViewModel ObsIntegration { get; }
        public ObsChatSettingsSectionViewModel ObsChat { get; }
        public BotLifecycleAutomationSectionViewModel BotLifecycle { get; }
        public PollsSettingsSectionViewModel Polls { get; }
        public RanksSettingsSectionViewModel Ranks { get; }
        public ChannelCatalogSettingsSectionViewModel ChannelCatalog { get; }
        public ChannelGroupsSettingsSectionViewModel ChannelGroups { get; }
        public DashboardLayoutSectionViewModel DashboardLayout { get; }
        public UpdateSettingsSectionViewModel Update { get; }
        public DebugChannelSectionViewModel DebugChannel { get; }
        public McpSettingsSectionViewModel Mcp { get; }
        public AppearanceSettingsSectionViewModel Appearance { get; }

        public Task CloseGateForAsync(params string[] fileNames)
        {
            return _gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => fileNames);
        }

        public void Dispose()
        {
            Page.Dispose();
        }
    }

    private sealed class FakeObsWebSocketClient : IObsWebSocketClient
    {
        public event EventHandler<ObsWebSocketEventArgs>? EventReceived
        {
            add { }
            remove { }
        }

        public bool IsConnected { get; private set; }

        public DateTimeOffset? LastExchangeAt { get; private set; }

        public Task<ObsConnectionSnapshot> ConnectAsync(ObsConnectionOptions options, CancellationToken cancellationToken)
        {
            IsConnected = true;
            LastExchangeAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new ObsConnectionSnapshot(true, "30.0.0", "5.5.0", null));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
        {
            LastExchangeAt = DateTimeOffset.UtcNow;
            var json = requestType switch
            {
                "GetSceneList" => """{"currentProgramSceneName":"Main","scenes":[{"sceneName":"Main"},{"sceneName":"BRB"}]}""",
                "GetInputList" => """{"inputs":[{"inputName":"Mic/Aux","inputKind":"wasapi_input_capture"},{"inputName":"Desktop Audio","inputKind":"wasapi_output_capture"},{"inputName":"ChatOverlay","inputKind":"browser_source"},{"inputName":"AlertsOverlay","inputKind":"browser_source"}]}""",
                _ => "{}",
            };
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult<JsonElement?>(doc.RootElement.Clone());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus { get; } = new FakeStreamStatus();
        public IPlatformMessenger Messenger => null!;
    }

    private sealed class FakeStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class FakeChannelAdapter(StreamingPlatform platform) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string input, string? contextId, CancellationToken ct) => Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken ct) => Task.FromResult(new ChannelAccessVerification(true, "1001", null));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken) => Task.FromResult(new ChannelLiveProbeResult(false, false, null));
        public IReadOnlyList<PlatformAccount>? ListAccounts() => null;
    }

    private sealed class FakeTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current => new("mainchannel", "mainchannel", false, true);
        public void BeginSession() { }
        public void EndSession() { }
    }

    private sealed class FakeUpdateCoordinator : KeepShell.Updates.IUpdateCoordinator
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public bool IsUpdatable => false;
        public KeepShell.Updates.UpdateStage Stage => KeepShell.Updates.UpdateStage.Idle;
        public UpdateCandidate? Candidate => null;
        public KeepShell.Updates.PendingUpdate? Pending => null;
        public int Progress => 0;
        public string? Error => null;
        public bool RestorePending() => false;
        public Task<bool> CheckAsync(bool manual, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> DownloadAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task CancelPendingDownloadsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool DiscardPending(Version? version = null) => false;
        public void Skip() { }
        public void Dismiss() { }
    }

    public sealed class FakeDialogService : IDialogService
    {
        public List<string> Warnings { get; } = [];
        public List<string> Infos { get; } = [];
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) => Infos.Add(message);
        public void Warning(string title, string message) => Warnings.Add(message);
        public void Error(string title, string message) => throw new InvalidOperationException(message);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
