using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
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
using KeepShell.Updates;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SettingsNavigationSaveTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Pending_save_preserves_the_draft_and_the_confirmed_destination(bool saveOnLeave)
    {
        await using var harness = new Harness();
        var (shell, settings, multistream, contributor) = (harness.Shell, harness.Settings, harness.Multistream, harness.Contributor);
        var settingsTab = shell.Selected;
        var multistreamTab = shell.FindSectionByKey(SectionKeys.Multistream);
        if (!saveOnLeave)
        {
            shell.Selected = multistreamTab;
        }

        var source = shell.Selected;
        var loads = contributor.LoadCount;

        settings.Dirty = true;
        if (saveOnLeave)
        {
            harness.Prompt.Answer = UnsavedChangesDecision.Save;
            shell.Selected = multistreamTab;
        }
        else
        {
            _ = settings.SaveCommand.ExecuteAsync(null);
        }

        var save = settings.SaveCommand.ExecutionTask!;
        try
        {
            harness.Prompt.Answer = UnsavedChangesDecision.Discard;
            shell.Selected = settingsTab;
            shell.Selected = multistreamTab;
            shell.Selected = shell.FindSectionByKey(SectionKeys.Logs);
            shell.OpenSettingsCommand.Execute(null);
            settings.OnEnter();
            multistream.OnEnter();
            multistream.OpenAutomation();
            settings.RevertCommand.Execute(null);
            multistream.DiscardUnsavedChanges();

            var concurrentSave = settings.TrySaveUnsavedChangesAsync();
            Assert.That(concurrentSave.IsCompletedSuccessfully, Is.True);
            Assert.That(await concurrentSave, Is.False);
            Assert.That(settings.SaveCommand.ExecuteAsync(null).IsCompletedSuccessfully, Is.True);
            Assert.That(await shell.RequestCloseAsync(), Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(shell.Selected, Is.SameAs(source));
                Assert.That(settings.HasUnsavedChanges, Is.True);
                Assert.That(contributor.LoadCount, Is.EqualTo(loads));
                Assert.That(contributor.SaveCount, Is.EqualTo(1));
                Assert.That(harness.Prompt.AskCount, Is.EqualTo(saveOnLeave ? 1 : 0));
            });
        }
        finally
        {
            contributor.CompleteSave(SettingsSectionSaveOutcome.Clean);
            await save;
        }

        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(shell.Selected, Is.SameAs(multistreamTab));
            Assert.That(settings.HasUnsavedChanges, Is.False);
            Assert.That(contributor.CollectedSettings, Is.SameAs(contributor.SavedSettings));
        });

        shell.Selected = shell.FindSectionByKey(SectionKeys.Logs);
        Assert.That(shell.Selected?.Key, Is.EqualTo(SectionKeys.Logs));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Failed_or_disposed_save_does_not_open_the_requested_page(bool disposeShell)
    {
        await using var harness = new Harness();
        var (shell, settings, multistream, contributor) = (harness.Shell, harness.Settings, harness.Multistream, harness.Contributor);
        var settingsTab = shell.Selected;
        var settingsSaved = 0;
        settings.SettingsSaved += (_, _) => settingsSaved++;
        settings.Dirty = true;
        harness.Prompt.Answer = UnsavedChangesDecision.Save;
        shell.Selected = shell.FindSectionByKey(SectionKeys.Multistream);
        var save = settings.SaveCommand.ExecutionTask!;

        if (disposeShell)
        {
            shell.Dispose();
        }

        contributor.CompleteSave(disposeShell
            ? SettingsSectionSaveOutcome.Clean
            : new(false, [], FailureMessage: "Не удалось переключить канал."));
        await save;
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(shell.Selected, Is.SameAs(settingsTab));
            Assert.That(multistream.IsAutomationExpanded, Is.False);
            Assert.That(settings.HasUnsavedChanges, Is.EqualTo(!disposeShell));
            Assert.That(settingsSaved, Is.EqualTo(disposeShell ? 1 : 0));
        });
    }

    [Test]
    public async Task SetTransientStatus_updates_status_and_restores_previous_status_when_timer_ticks()
    {
        await using var harness = new Harness();
        var shell = harness.Shell;
        var initialStatus = shell.StatusText;
        var initialSeverity = shell.StatusSeverity;

        shell.SetTransientStatus("Этого пользователя нет в статистике", StatusSeverity.Info, TimeSpan.FromMilliseconds(50));

        Assert.Multiple(() =>
        {
            Assert.That(shell.StatusText, Is.EqualTo("Этого пользователя нет в статистике"));
            Assert.That(shell.StatusSeverity, Is.EqualTo(StatusSeverity.Info));
        });

        await Task.Delay(150);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.Background,
            static () => { });

        Assert.Multiple(() =>
        {
            Assert.That(shell.StatusText, Is.EqualTo(initialStatus));
            Assert.That(shell.StatusSeverity, Is.EqualTo(initialSeverity));
        });
    }

    [Test]
    public async Task Navigation_exposes_channels_and_profiles_and_routes_chat_youtube_setup_in_one_click()
    {
        await using var harness = new Harness();
        var shell = harness.Shell;
        var multistreamSection = shell.FindSectionByKey(SectionKeys.Multistream);

        Assert.Multiple(() =>
        {
            Assert.That(multistreamSection?.Title, Is.EqualTo("Каналы и профили"));
            Assert.That(shell.FindSectionByKey("Мультистрим"), Is.SameAs(multistreamSection));
            Assert.That(shell.FindSectionByKey("Каналы и профили"), Is.SameAs(multistreamSection));
        });

        harness.ChatTile.SecondaryChat.FallbackActionCommand!.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(shell.Selected, Is.SameAs(multistreamSection));
            Assert.That(harness.Multistream.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
        });
    }

    [Test]
    public async Task ToggleConnect_requires_two_step_confirmation_and_disarms_on_timeout_cancel_or_external_disconnect()
    {
        await using var harness = new Harness();
        var shell = harness.Shell;

        await harness.Bus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Connected));
        Assert.That(shell.Phase, Is.EqualTo(BotLifecyclePhase.Connected));
        Assert.That(shell.ConnectButtonText, Is.EqualTo("Отключить бота"));

        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(shell.Phase, Is.EqualTo(BotLifecyclePhase.Connected), "Первый клик не должен отключать бота.");
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.True);
            Assert.That(shell.ConnectButtonText, Is.EqualTo("Точно отключить?"));
            Assert.That(harness.Connection.StopCalls, Is.Zero);
        });

        foreach (var timer in harness.Dispatcher.Timers)
        {
            timer.Tick();
        }

        Assert.Multiple(() =>
        {
            Assert.That(shell.Phase, Is.EqualTo(BotLifecyclePhase.Connected));
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.False);
            Assert.That(shell.ConnectButtonText, Is.EqualTo("Отключить бота"));
            Assert.That(harness.Connection.StopCalls, Is.Zero);
        });

        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.That(shell.IsDisconnectConfirmationArmed, Is.True);
        shell.CancelDisconnectConfirmationCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(shell.Phase, Is.EqualTo(BotLifecyclePhase.Connected));
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.False);
            Assert.That(harness.Connection.StopCalls, Is.Zero);
        });

        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.That(shell.IsDisconnectConfirmationArmed, Is.True);
        await harness.Bus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Idle));
        Assert.Multiple(() =>
        {
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.False);
            Assert.That(harness.Connection.StopCalls, Is.Zero);
        });

        await harness.Bus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Connected));
        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.That(shell.IsDisconnectConfirmationArmed, Is.True);
        harness.Time.Advance(TimeSpan.FromSeconds(3.1));
        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.True, "Просроченный клик без тика таймера не должен отключать бота.");
            Assert.That(harness.Connection.StopCalls, Is.Zero);
        });

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await shell.ToggleConnectCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(shell.IsDisconnectConfirmationArmed, Is.False);
            Assert.That(harness.Connection.StopCalls, Is.EqualTo(1));
        });
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("poproshayka-settings-navigation-");
        private readonly BotConnectionManager _innerConnection;
        private readonly DashboardViewModel _overview;
        private readonly StreamHistoryPageViewModel _history;
        private readonly MultistreamTileViewModel _tile;
        private readonly ChatDisplayTileViewModel _chatTile;
        private readonly UpdateBannerViewModel _update;
        private readonly OnboardingBannerViewModel _onboarding;

        public Harness()
        {
            Bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var bus = Bus;
            var gate = new SettingsWriteGate();
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var store = new MemorySettings();
            var windows = DispatchProxy.Create<IMainWindowController, InertProxy>();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
            Dispatcher = new ManualUiDispatcher();
            Time = new ManualTimeProvider();
            var dispatcher = Dispatcher;
            var accounts = new AccountsStore(filePath: Path.Combine(_root.FullName, "accounts.json"));
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root.FullName, "channels.json"));
            var youtube = new YouTubeSettingsStore(filePath: Path.Combine(_root.FullName, "youtube.json"));
            Settings = new([Contributor], manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, store);

            var youtubePlatform = new StubPlatform(StreamingPlatform.YouTube, "YouTube", isEnabled: false);
            var twitchPlatform = new StubPlatform(StreamingPlatform.Twitch, "Twitch", isEnabled: true);
            var registry = new ChatPlatformRegistry([twitchPlatform, youtubePlatform]);
            _innerConnection = new(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            Connection = new(_innerConnection);
            _tile = new(registry, catalog, _innerConnection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtube);
            _chatTile = new(
                manager,
                new TargetChannelProvider(manager, new DebugChannelStore(filePath: Path.Combine(_root.FullName, "debug-chat.json")), DebugChannelOverride.None),
                NullLogger<ChatDisplayTileViewModel>.Instance,
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                dialogs,
                bus,
                new PoproshaykaBot.Core.Chat.Display.ChatDisplayStore(NullLogger<PoproshaykaBot.Core.Chat.Display.ChatDisplayStore>.Instance),
                registry,
                youtube,
                dispatcher,
                store);
            Multistream = new(Settings, _tile, []);
            var layout = new DashboardLayoutStore(filePath: Path.Combine(_root.FullName, "layout.json"));
            _overview = new([_tile, _chatTile], new DashboardLayoutCoordinator(layout), TimeProvider.System);
            var history = new BroadcastHistoryStore(null, Path.Combine(_root.FullName, "broadcasts.json"), Path.Combine(_root.FullName, "sessions.json"));
            var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);
            _history = new(history, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance), store, boxArt, bus);
            _update = new(
                DispatchProxy.Create<KeepShell.Updates.IUpdateCoordinator, InertProxy>(),
                DispatchProxy.Create<IUpdateEnvironment, InertProxy>(),
                dialogs,
                NullLogger<UpdateBannerViewModel>.Instance,
                dispatcher,
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                windows);
            _onboarding = new(new OnboardingChecklist(manager, accounts), bus);
            var debug = new DebugBannerViewModel(new TargetChannelProvider(manager,
                new DebugChannelStore(filePath: Path.Combine(_root.FullName, "debug.json")), DebugChannelOverride.None));
            var preferences = new ShellPreferences(store) { LastPage = SectionKeys.Settings };
            Shell = new(new ModalHostViewModel(dispatcher), _overview, Settings, null!, _history, null!, null!, null!, null!,
                preferences, Connection, bus, NullLogger<ShellViewModel>.Instance, dialogs, _update, _onboarding, debug,
                null!, null!, store, dispatcher, Prompt, windows, multistreamPage: Multistream, timeProvider: Time);
        }

        public InMemoryEventBus Bus { get; }
        public ManualUiDispatcher Dispatcher { get; }
        public ManualTimeProvider Time { get; }
        public RecordingBotConnectionController Connection { get; }
        public PendingContributor Contributor { get; } = new();
        public FakeUnsavedChangesPrompt Prompt { get; } = new();
        public SettingsPageViewModel Settings { get; }
        public MultistreamPageViewModel Multistream { get; }
        public ChatDisplayTileViewModel ChatTile => _chatTile;
        public ShellViewModel Shell { get; }

        public async ValueTask DisposeAsync()
        {
            Shell.Dispose();
            _onboarding.Dispose();
            _update.Dispose();
            _history.Dispose();
            _overview.Dispose();
            _chatTile.Dispose();
            _tile.Dispose();
            Settings.Dispose();
            await _innerConnection.DisposeAsync();
            _root.Delete(recursive: true);
        }
    }

    private sealed class RecordingBotConnectionController(BotConnectionManager inner) : IBotConnectionController
    {
        public int StopCalls { get; private set; }
        public bool IsBusy => inner.IsBusy;
        public BotLifecyclePhase CurrentPhase => inner.CurrentPhase;
        public bool IsPlatformBusy(StreamingPlatform platform) => inner.IsPlatformBusy(platform);
        public BotLifecyclePhase GetPhase(StreamingPlatform platform) => inner.GetPhase(platform);
        public void StartConnection() => inner.StartConnection();
        public void StartConnection(StreamingPlatform platform) => inner.StartConnection(platform);
        public void CancelConnection() => inner.CancelConnection();
        public void CancelConnection(StreamingPlatform platform) => inner.CancelConnection(platform);
        public Task WaitForConnectionAsync() => inner.WaitForConnectionAsync();
        public Task WaitForConnectionAsync(StreamingPlatform platform) => inner.WaitForConnectionAsync(platform);
        public Task StopAsync(BotStopMode mode = BotStopMode.Graceful)
        {
            StopCalls++;
            return inner.StopAsync(mode);
        }

        public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful)
        {
            StopCalls++;
            return inner.StopAsync(platform, mode);
        }
    }

    private sealed class PendingContributor : ISettingsSectionContributor
    {
        private readonly TaskCompletionSource<SettingsSectionSaveOutcome> _save = new();

        public int Order => 0;
        public string SectionKey => "pending";
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public AppSettings? SavedSettings { get; private set; }
        public AppSettings? CollectedSettings { get; private set; }

        public SettingsSection CreateSection() => new(SectionKey, "Проверка", PackIconLucideKind.Settings, string.Empty);
        public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks() => [];
        public void Load(SettingsSessionState session) => LoadCount++;
        public void BeginSave(AppSettings targetSettings) { }
        public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState) => new EmptySubscription();

        public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
        {
            SaveCount++;
            SavedSettings = targetSettings;
            return _save.Task;
        }

        public IReadOnlyList<SettingsSectionEvent> CollectPostSaveEvents(AppSettings targetSettings)
        {
            CollectedSettings = targetSettings;
            return [];
        }

        public void CompleteSave(SettingsSectionSaveOutcome outcome) => _save.SetResult(outcome);
    }

    private sealed class EmptySubscription : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class StubPlatform(StreamingPlatform id, string name, bool isEnabled) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => name;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod is not null
                && targetMethod.ReturnType != typeof(void)
                && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
