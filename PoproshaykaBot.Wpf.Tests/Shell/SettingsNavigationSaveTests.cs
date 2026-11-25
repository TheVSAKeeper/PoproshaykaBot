using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Update;
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
        var openings = 0;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        multistream.SectionRequested += (_, _) =>
        {
            openings++;
            opened.TrySetResult();
        };

        settings.Dirty = true;
        if (saveOnLeave)
        {
            harness.Prompt.Answer = UnsavedChangesDecision.Save;
            settings.NavigateToMultistreamCommand.Execute(null);
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
            settings.NavigateToMultistreamCommand.Execute(null);
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
                Assert.That(openings, Is.Zero);
            });
        }
        finally
        {
            contributor.CompleteSave(SettingsSectionSaveOutcome.Clean);
            await save;
        }

        if (saveOnLeave)
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Multiple(() =>
        {
            Assert.That(shell.Selected, Is.SameAs(multistreamTab));
            Assert.That(settings.HasUnsavedChanges, Is.False);
            Assert.That(contributor.CollectedSettings, Is.SameAs(contributor.SavedSettings));
            Assert.That(openings, Is.EqualTo(saveOnLeave ? 1 : 0));
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
        settings.NavigateToMultistreamCommand.Execute(null);
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

    private sealed class Harness : IAsyncDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("poproshayka-settings-navigation-");
        private readonly BotConnectionManager _connection;
        private readonly DashboardViewModel _overview;
        private readonly StreamHistoryPageViewModel _history;
        private readonly MultistreamTileViewModel _tile;
        private readonly UpdateBannerViewModel _update;
        private readonly OnboardingBannerViewModel _onboarding;

        public Harness()
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var gate = new SettingsWriteGate();
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var store = new MemorySettings();
            var windows = DispatchProxy.Create<IMainWindowController, InertProxy>();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
            var dispatcher = new ManualUiDispatcher();
            var accounts = new AccountsStore(filePath: Path.Combine(_root.FullName, "accounts.json"));
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root.FullName, "channels.json"));
            var youtube = new YouTubeSettingsStore(filePath: Path.Combine(_root.FullName, "youtube.json"));
            Settings = new([Contributor], manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, store);

            _connection = new(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            _tile = new(new ChatPlatformRegistry([]), catalog, _connection, new PlatformAuthRegistry([]), [], bus, dispatcher, youtube);
            Multistream = new(Settings, _tile, []);
            var layout = new DashboardLayoutStore(filePath: Path.Combine(_root.FullName, "layout.json"));
            _overview = new([_tile], new DashboardLayoutCoordinator(layout), TimeProvider.System);
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
            _onboarding = new(new OnboardingChecklist(manager, accounts), DispatchProxy.Create<IOnboardingWizardLauncher, InertProxy>(), bus);
            var debug = new DebugBannerViewModel(new TargetChannelProvider(manager,
                new DebugChannelStore(filePath: Path.Combine(_root.FullName, "debug.json")), DebugChannelOverride.None));
            var preferences = new ShellPreferences(store) { LastPage = SectionKeys.Settings };
            Shell = new(new ModalHostViewModel(dispatcher), _overview, Settings, null!, _history, null!, null!, null!, null!,
                preferences, _connection, bus, NullLogger<ShellViewModel>.Instance, dialogs, _update, _onboarding, debug,
                null!, null!, store, dispatcher, Prompt, windows, multistreamPage: Multistream);
        }

        public PendingContributor Contributor { get; } = new();
        public FakeUnsavedChangesPrompt Prompt { get; } = new();
        public SettingsPageViewModel Settings { get; }
        public MultistreamPageViewModel Multistream { get; }
        public ShellViewModel Shell { get; }

        public async ValueTask DisposeAsync()
        {
            Shell.Dispose();
            _onboarding.Dispose();
            _update.Dispose();
            _history.Dispose();
            _overview.Dispose();
            _tile.Dispose();
            Settings.Dispose();
            await _connection.DisposeAsync();
            _root.Delete(recursive: true);
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
