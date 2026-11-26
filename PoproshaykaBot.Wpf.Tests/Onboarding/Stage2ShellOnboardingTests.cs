using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Onboarding;
using PoproshaykaBot.Wpf.Views.Settings;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class Stage2ShellOnboardingTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/RatingStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
    ];

    private string _tempDir = string.Empty;

    [OneTimeSetUp]
    public void EnsureApplication() => TestResources.Ensure(Dictionaries);

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stage2-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void R32_And_R34_WelcomePageExplainsBothPathsWithoutArrows()
    {
        var page = new WelcomePageViewModel();
        var context = new OnboardingContext(new AppSettings(), new(), new()) { IncludeLegacyImport = true };
        page.OnEnter(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.LegacyPathTitle, Does.Contain("Перенести профиль"));
            Assert.That(page.LegacyPathDescription, Does.Contain("папку со старой версией"));
            Assert.That(page.ScratchPathTitle, Does.Contain("Настроить с нуля"));
            Assert.That(page.ScratchTwitchText, Does.Contain("Developer Console"));
            Assert.That(page.ScratchTwitchText, Does.Contain("аккаунт бота"));
            Assert.That(page.ScratchYouTubeText, Does.Contain("Google Cloud"));
            Assert.That(page.ScratchYouTubeText, Does.Contain("аккаунт бота"));
            Assert.That(page.HintText, Does.Not.Contain("→").And.Not.Contain("←"));
            Assert.That(page.HintText, Does.Not.Contain("Далее"));
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void R32_And_R12_WelcomeViewScrollsCleanlyAt640x480AndHasAccessibleName()
    {
        var page = new WelcomePageViewModel();
        page.OnEnter(new OnboardingContext(new AppSettings(), new(), new()) { IncludeLegacyImport = true });

        var view = new WelcomePageView { DataContext = page };
        view.Measure(new Size(600, 320));
        view.Arrange(new Rect(0, 0, 600, 320));
        view.UpdateLayout();

        var scroll = FindVisualChild<ScrollViewer>(view);
        Assert.That(scroll, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scroll!.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));
            Assert.That(AutomationProperties.GetName(scroll), Is.EqualTo("Добро пожаловать"));
        }
    }

    [Test]
    public async Task R34_And_R47_WizardHidesBackButtonOnFirstStepAndRemovesArrows()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        var platforms = new PlatformsPageViewModel();
        using var wizard = new OnboardingWizardViewModel(
            [welcome, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wizard.ShowBackButton, Is.False);
            Assert.That(wizard.NextButtonText, Is.EqualTo("Далее"));
            Assert.That(wizard.NextButtonText, Does.Not.Contain("→").And.Not.Contain("←"));
        }

        await wizard.NextCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wizard.ShowBackButton, Is.True);
            Assert.That(wizard.NextButtonText, Is.EqualTo("Готово"));
        }
    }

    [Test]
    public async Task R47_And_R37_And_R12_WizardWindowLayoutSeparatesExitAndFocusesPageHeader()
    {
        var previousSyncContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        var platforms = new PlatformsPageViewModel();
        using var wizardVm = new OnboardingWizardViewModel(
            [welcome, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        var window = new OnboardingWizardWindow(wizardVm);
        OffScreenWindow.Prepare(window, 640, 480);
        window.Show();
        try
        {
            await PumpDispatcherAsync();

            var backButton = FindVisualChildren<Button>(window).First(b => Equals(b.Content, "Назад"));
            var deferButton = FindVisualChildren<Button>(window).First(b => Equals(b.Content, "Отложить настройку"));
            var nextButton = FindVisualChildren<Button>(window).First(b => Equals(b.Content, "Далее"));
            var headerTitle = window.FindName("PageHeaderTitle") as TextBlock;

            Assert.That(headerTitle, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(backButton.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(deferButton.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(nextButton.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(headerTitle!.Focusable, Is.True);
                Assert.That(AutomationProperties.GetName(headerTitle), Is.EqualTo("Добро пожаловать"));
                Assert.That(Keyboard.FocusedElement, Is.SameAs(headerTitle));
            }

            var deferPos = deferButton.TranslatePoint(new Point(0, 0), window);
            var nextPos = nextButton.TranslatePoint(new Point(0, 0), window);
            Assert.That(nextPos.X - (deferPos.X + deferButton.ActualWidth), Is.GreaterThan(40),
                "Кнопка выхода должна быть отделена от кнопки основного перехода");

            await wizardVm.NextCommand.ExecuteAsync(null);
            await PumpDispatcherAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(backButton.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(Keyboard.FocusedElement, Is.SameAs(headerTitle));
                Assert.That(AutomationProperties.GetName(headerTitle!), Is.EqualTo("Площадки трансляции"));
            }
        }
        finally
        {
            window.Close();
            await PumpDispatcherAsync();
        }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
        }
    }

    [Test]
    public async Task R17_UntouchedCancelClosesWithoutConfirmWhileModifiedShowsConsequencesAndRespectsDecline()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();
        var dialogs = new RecordingDialogService { ConfirmResult = false };

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var uiStore = new MemorySettings();
        using var theme = new ThemeSelectionViewModel(uiStore);
        using var appearance = new AppearancePageViewModel(theme, new ShellPreferences(uiStore), uiStore);
        var platforms = new PlatformsPageViewModel();

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, appearance, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            dialogService: dialogs);

        var dismissed = false;
        var source = CreateSource(_tempDir);
        wizard.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [source],
            Target = null,
            OnLegacyImportSkipped = v => dismissed = v,
        });

        importPage.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            v => dismissed = v,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [],
            });

        Assert.That(await wizard.RequestCloseAsync(confirmIfModified: true), Is.True);
        Assert.That(dialogs.ConfirmWarningCalls, Is.EqualTo(0), "На нетронутом первом шаге подтверждение не требуется");

        using var wizard2 = new OnboardingWizardViewModel(
            [new WelcomePageViewModel(), importPage, appearance, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            dialogService: dialogs);

        wizard2.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [source],
            Target = null,
            OnLegacyImportSkipped = v => dismissed = v,
        });

        await wizard2.NextCommand.ExecuteAsync(null);
        importPage.Import!.NeverAskAgain = true;
        await wizard2.NextCommand.ExecuteAsync(null);
        appearance.Appearance.Theme.Select(ThemeManager.DefaultDark.Key);
        await wizard2.NextCommand.ExecuteAsync(null);
        platforms.YouTubeEnabled = true;

        Assert.That(await wizard2.RequestCloseAsync(confirmIfModified: true), Is.False, "При отказе в диалоге мастер не должен закрываться");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wizard2.IsPageInteractive, Is.True);
            Assert.That(dialogs.ConfirmWarningCalls, Is.EqualTo(1));
            Assert.That(dialogs.LastMessage, Does.Contain("несохранённый ввод"));
            Assert.That(dialogs.LastMessage, Does.Contain("темы и размера текста"));
            Assert.That(dialogs.LastMessage, Does.Contain("Больше не предлагать"));
            Assert.That(dismissed, Is.False);
        }

        dialogs.ConfirmResult = true;
        Assert.That(await wizard2.RequestCloseAsync(confirmIfModified: true), Is.True);
        Assert.That(dismissed, Is.False, "При отмене мастера «Больше не предлагать» не должно сохраняться");
    }

    [Test]
    public async Task R17_ConfirmedImportShowsWhatRemainsOnCancel()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();
        var dialogs = new RecordingDialogService { ConfirmResult = true };

        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        using var wizard = new OnboardingWizardViewModel(
            [new WelcomePageViewModel(), importPage, new PlatformsPageViewModel()],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            dialogService: dialogs);

        var source = CreateSource(_tempDir);
        importPage.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            null,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [],
            });

        await wizard.NextCommand.ExecuteAsync(null);
        importPage.Import!.SelectedSource = importPage.Import.Sources[0];
        await importPage.Import.ImportCommand.ExecuteAsync(null);

        Assert.That(await wizard.RequestCloseAsync(confirmIfModified: true), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialogs.ConfirmWarningCalls, Is.EqualTo(1));
            Assert.That(dialogs.LastMessage, Does.Contain("перенесённые файлы"));
        }
    }

    [Test]
    public async Task R33_AppearanceHidesChromeAndToastSwitchesInWizardOnly()
    {
        var previousSyncContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var store = new MemorySettings();
            var shell = new ShellPreferences(store);
            using var theme = new ThemeSelectionViewModel(store);

            var settingsSection = new AppearanceSettingsSectionViewModel(theme, shell);
            using var wizardPage = new AppearancePageViewModel(theme, shell, store);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settingsSection.ShowChromeAndNotificationOptions, Is.True);
                Assert.That(wizardPage.Appearance.ShowChromeAndNotificationOptions, Is.False);
            }

            var settingsView = new AppearanceSettingsView { DataContext = settingsSection };
            var settingsWindow = new Window { Content = settingsView };
            OffScreenWindow.Prepare(settingsWindow, 600, 600);
            settingsWindow.Show();

            var wizardView = new AppearancePageView { DataContext = wizardPage };
            var wizardWindow = new Window { Content = wizardView };
            OffScreenWindow.Prepare(wizardWindow, 600, 600);
            wizardWindow.Show();

            try
            {
                await PumpDispatcherAsync();

                var settingsOptionsPanel = settingsView.FindName("ChromeAndNotificationOptionsPanel") as UIElement;
                var wizardAppearanceView = FindVisualChild<AppearanceSettingsView>(wizardView)!;
                var wizardOptionsPanel = wizardAppearanceView.FindName("ChromeAndNotificationOptionsPanel") as UIElement;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(settingsOptionsPanel, Is.Not.Null);
                    Assert.That(wizardOptionsPanel, Is.Not.Null);
                    Assert.That(settingsOptionsPanel!.Visibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(wizardOptionsPanel!.Visibility, Is.EqualTo(Visibility.Collapsed));
                }
            }
            finally
            {
                wizardWindow.Close();
                settingsWindow.Close();
                await PumpDispatcherAsync();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void R31_And_R12_PlatformsCardsAreFullCardCheckBoxesWithAccessibleNames()
    {
        var page = new PlatformsPageViewModel();
        page.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));

        var view = new PlatformsPageView { DataContext = page };
        view.Measure(new Size(688, 400));
        view.Arrange(new Rect(0, 0, 688, 400));
        view.UpdateLayout();

        var cards = FindVisualChildren<CheckBox>(view)
            .Where(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c)))
            .ToList();

        Assert.That(cards, Has.Count.EqualTo(2));
        var twitchCard = cards[0];
        var youtubeCard = cards[1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AutomationProperties.GetName(twitchCard), Is.EqualTo("Включить Twitch"));
            Assert.That(AutomationProperties.GetName(youtubeCard), Is.EqualTo("Включить YouTube"));
            Assert.That(twitchCard.ActualWidth, Is.GreaterThan(300));
            Assert.That(twitchCard.ActualHeight, Is.GreaterThan(40));
            Assert.That(youtubeCard.ActualWidth, Is.GreaterThan(300));
            Assert.That(youtubeCard.ActualHeight, Is.GreaterThan(40));
        }

        youtubeCard.IsChecked = true;
        Assert.That(page.YouTubeEnabled, Is.True);
    }

    [Test]
    public async Task R30_ShellDisablesConnectWhenProfileIncompleteAndReenablesAfterSetup()
    {
        var data = Directory.CreateTempSubdirectory("stage2-r30-");
        try
        {
            var gate = new SettingsWriteGate();
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var accounts = new AccountsStore(filePath: Path.Combine(data.FullName, "acc.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new MemorySettings();
            var dispatcher = new ManualUiDispatcher();
            var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
            var windows = DispatchProxy.Create<IMainWindowController, InertProxy>();
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var targetChannel = new TargetChannelProvider(manager, new DebugChannelStore(filePath: Path.Combine(data.FullName, "dbg.json")), DebugChannelOverride.None);
            var basic = new BasicSettingsSectionViewModel();
            using var settings = new SettingsPageViewModel(
                [new BasicSettingsSectionContributor(basic, new(), new(), manager, targetChannel)],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, store);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            var registry = new ChatPlatformRegistry([]);
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
            var wizardLauncher = new TestWizardLauncher();
            using var onboardingBanner = new OnboardingBannerViewModel(new OnboardingChecklist(manager, accounts), wizardLauncher, bus);
            var debugBanner = new DebugBannerViewModel(targetChannel);
            var preferences = new ShellPreferences(store) { LastPage = SectionKeys.Settings };

            using var shell = new ShellViewModel(
                new ModalHostViewModel(dispatcher), overview, settings, null!, streamHistory, null!, null!, null!, null!,
                preferences, connection, bus, NullLogger<ShellViewModel>.Instance, dialogs, updateBanner, onboardingBanner, debugBanner,
                null!, null!, store, dispatcher, DispatchProxy.Create<IUnsavedChangesPrompt, InertProxy>(), windows, registry);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.ToggleConnectCommand.CanExecute(null), Is.False);
                Assert.That(shell.IsConnectBlockedBySetup, Is.True);
                Assert.That(shell.ConnectButtonTooltip, Does.Contain("Не настроено:"));
            }

            await bus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Connecting));
            Assert.That(shell.ToggleConnectCommand.CanExecute(null), Is.True, "Отмена подключения должна оставаться доступной");

            await bus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Idle));
            Assert.That(shell.ToggleConnectCommand.CanExecute(null), Is.False);

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
            accounts.SaveAll(new TwitchAccountSettings
            {
                AccessToken = "bot",
                RefreshToken = "ref",
                StoredScopes = [.. TwitchScopes.BotRequired],
            }, new());
            wizardLauncher.RaiseClosed();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.IsConnectBlockedBySetup, Is.False);
                Assert.That(shell.ToggleConnectCommand.CanExecute(null), Is.True);
            }
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    private static async Task PumpDispatcherAsync()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        await Task.CompletedTask;
    }

    private static LegacyDataSummary CreateSource(string path) => new()
    {
        SourcePath = path,
        Kind = LegacyDataSourceKind.SelectedFolder,
        HasMonolithicSettings = false,
        HasSettingsDirectory = true,
        HasOAuthTokens = false,
        Files = [new("settings.json", Path.Combine(path, "settings.json"), "settings/settings.json", 10, DateTime.UtcNow)],
    };

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject =>
        FindVisualChildren<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services
            .AddCoreInfrastructure(new UiLogSink(), disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannelOverride.None)
            .AddTwitchClients()
            .AddGameBoxArt(cacheOnly: true)
            .AddUserAvatars(cacheOnly: true)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate()
            .AddDiagnostics();
        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(sp => sp.GetRequiredService<BotConnectionManager>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class StubFilePicker(string folder) : IFilePicker
    {
        public string? PickFolder(string title, string? initialDirectory = null) => folder;
        public string? PickFile(FileOpenRequest request) => null;
        public string? SaveFile(FileSaveRequest request) => null;
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; }
        public int ConfirmWarningCalls { get; private set; }
        public string LastMessage { get; private set; } = string.Empty;

        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => ConfirmResult;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false)
        {
            ConfirmWarningCalls++;
            LastMessage = message;
            return ConfirmResult;
        }
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class TestWizardLauncher : IOnboardingWizardLauncher
    {
        public event EventHandler? Closed;
        public bool HasBeenShown { get; private set; }
        public void Show(OnboardingStep step = OnboardingStep.None) => HasBeenShown = true;
        public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }

#pragma warning disable CA1852
    private class InertProxy : DispatchProxy
#pragma warning restore CA1852
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || targetMethod.ReturnType == typeof(void))
            {
                return null;
            }

            if (targetMethod.ReturnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
