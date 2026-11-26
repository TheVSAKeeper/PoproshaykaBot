using System.IO;
using System.Net.Http;
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
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
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
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class MultistreamOnboardingWizardTests
{
    private string _tempDir = string.Empty;

    [OneTimeSetUp]
    public void RegisterPackUris() => PackScheme.Ensure();

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ms-onboard-{Guid.NewGuid():N}");
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
    public void PlatformsPage_RequiresAtLeastOnePlatformAndRaisesPlatformsChanged()
    {
        var page = new PlatformsPageViewModel();
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true } },
            new(),
            new());
        var changedCount = 0;
        context.PlatformsChanged += (_, _) => changedCount++;

        page.OnEnter(context);
        Assert.That(page.CanAdvance, Is.True);

        page.TwitchEnabled = false;
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.HasValidationError, Is.True);
            Assert.That(context.Settings.Twitch.Enabled, Is.False);
            Assert.That(changedCount, Is.EqualTo(1));
        });

        page.YouTubeEnabled = true;
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.HasValidationError, Is.False);
            Assert.That(context.YouTubeSettings.Enabled, Is.True);
            Assert.That(changedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void PlatformsPage_ShowsAccurateReadinessBadges()
    {
        var page = new PlatformsPageViewModel();
        var context = new OnboardingContext(
            new AppSettings
            {
                Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec", Channel = "chan" },
            },
            new TwitchAccountSettings
            {
                AccessToken = "tok",
                RefreshToken = "ref",
                StoredScopes = [.. TwitchScopes.BotRequired],
            },
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-cid" });

        page.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(page.TwitchStatusText, Is.EqualTo("Настроен"));
            Assert.That(page.YouTubeStatusText, Is.EqualTo("Требует донастройки"));
        });
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public async Task LegacyImportPage_ShowsReadinessAndImmediateFinishOnlyWhenProfileComplete(
        bool hasChanges, bool completeProfile, bool expectsFinishButton)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var checklist = new OnboardingChecklist(settings, accounts);

        if (completeProfile)
        {
            settings.SaveSettings(new AppSettings
            {
                Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec", Channel = "streamer" },
            });
            accounts.SaveAll(new TwitchAccountSettings
            {
                AccessToken = "bot",
                RefreshToken = "ref",
                StoredScopes = [.. TwitchScopes.BotRequired],
            }, new());
        }

        var context = new OnboardingContext(settings.Current, new(), new())
        {
            IncludeLegacyImport = true,
            Checklist = checklist,
        };

        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var finishRequested = false;
        page.FinishRequested += () => finishRequested = true;

        var source = new LegacyDataSummary
        {
            SourcePath = _tempDir,
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Files = [new("settings.json", Path.Combine(_tempDir, "settings.json"), "settings/settings.json", 10, DateTime.UtcNow)],
        };

        page.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            null,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = hasChanges ? ["settings/settings.json"] : [],
                SkippedFiles = [],
                Failures = [],
            });

        page.OnEnter(context);
        Assert.That(page.IsApplicable(context), Is.True);

        page.Import!.SelectedSource = page.Import.Sources[0];
        await page.Import.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.HasCompletedImport, Is.True);
            Assert.That(page.IsProfileComplete, Is.EqualTo(expectsFinishButton));
            Assert.That(page.FinishImmediatelyCommand.CanExecute(null), Is.EqualTo(expectsFinishButton));
            Assert.That(page.HasMissingItems, Is.EqualTo(hasChanges && !completeProfile));
        });

        if (expectsFinishButton)
        {
            page.FinishImmediatelyCommand.Execute(null);
            Assert.That(finishRequested, Is.True);
        }
    }

    [Test]
    public async Task LegacyImportPage_SkippingWithNeverAskAgainCommitsDismissOnlyOnWizardCompletion()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();
        var dismissed = false;

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var platforms = new PlatformsPageViewModel();
        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        wizard.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [],
            Target = null,
            OnLegacyImportSkipped = value => dismissed = value,
        });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(importPage));
        importPage.Import!.NeverAskAgain = true;

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(wizard.CurrentPage, Is.SameAs(platforms));
            Assert.That(dismissed, Is.False, "Переход «Далее» не должен заранее сохранять «Больше не предлагать»");
        });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(dismissed, Is.True, "При завершении мастера «Больше не предлагать» должно сохраниться");
    }

    [Test]
    public async Task Wizard_DynamicallyFiltersPagesAndRollsBackTwitchEnabled()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings { Twitch = new TwitchSettings { Enabled = true, ClientId = "orig" } });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var platforms = new PlatformsPageViewModel();
        using var credentials = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());
        using var ytEditor = new YouTubeSettingsSectionViewModel(
            new StubYouTubeOAuthService(),
            new StubLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);
        using var youtube = new YouTubeOnboardingPageViewModel(ytEditor);
        using var completion = new CompletionPageViewModel(
            settings,
            accounts,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            controller,
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService());

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, platforms, credentials, youtube, completion],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(wizard.PageCount, Is.EqualTo(4), "Без IncludeLegacyImport и YouTube должны быть Welcome, Platforms, Credentials, Completion");
            Assert.That(wizard.StepIndicator, Is.EqualTo("Шаг 1 из 4"));
        });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.StepIndicator, Is.EqualTo("Шаг 2 из 4"));

        platforms.TwitchEnabled = false;
        platforms.YouTubeEnabled = true;

        Assert.Multiple(() =>
        {
            Assert.That(wizard.PageCount, Is.EqualTo(4), "YouTube-only: Welcome, Platforms, YouTube, Completion");
            Assert.That(wizard.StepIndicator, Is.EqualTo("Шаг 2 из 4"));
        });

        wizard.StartAt(OnboardingStep.YouTube);
        Assert.That(wizard.StepIndicator, Is.EqualTo("Шаг 3 из 4"));

        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var saveContext = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-id" });
        Assert.That(saveContext.SaveSetup(settings, accounts, ytSettingsStore, ytAccountsStore), Is.Null);
        Assert.That(settings.Current.Twitch.Enabled, Is.False);

        wizard.StartAt(OnboardingStep.Platforms);
        settings.Mutate(s => s.Twitch.Enabled = false);
        Assert.That(await wizard.RequestCloseAsync(), Is.True);
    }

    [Test]
    public async Task Wizard_CompleteContainerReloadRefreshesBaselineAndNextPages()
    {
        await using var oldProvider = BuildProvider();
        var oldSettings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var oldAccounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = oldProvider.GetRequiredService<IEventBus>();
        var controller = oldProvider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var oldPlatforms = new PlatformsPageViewModel();

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, oldPlatforms],
            oldSettings,
            oldAccounts,
            oldProvider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        var importedDir = Path.Combine(_tempDir, "imported");
        Directory.CreateDirectory(importedDir);
        var newSettings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(importedDir, "settings.json"));
        newSettings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { Enabled = false, ClientId = "imported-twitch" },
        });
        var newAccounts = new AccountsStore(filePath: Path.Combine(importedDir, "accounts.json"));
        var newYtSettings = new YouTubeSettingsStore(filePath: Path.Combine(importedDir, "youtube.json"));
        newYtSettings.Save(new YouTubeSettings { Enabled = true, ClientId = "imported-yt", Channel = "UC1234567890123456789012" });
        var newYtAccounts = new YouTubeAccountsStore(filePath: Path.Combine(importedDir, "youtube-accounts.json"));
        var newCatalog = new ChannelCatalogStore(filePath: Path.Combine(importedDir, "channels.json"));
        var newChecklist = new OnboardingChecklist(newSettings, newAccounts, newCatalog, youtubeSettings: newYtSettings, youtubeAccounts: newYtAccounts);

        var newServices = new ServiceCollection();
        newServices.AddSingleton(newSettings);
        newServices.AddSingleton(newAccounts);
        newServices.AddSingleton(oldProvider.GetRequiredService<KestrelHttpServer>());
        newServices.AddSingleton(controller);
        newServices.AddSingleton(bus);
        newServices.AddSingleton(newYtSettings);
        newServices.AddSingleton(newYtAccounts);
        newServices.AddSingleton(newCatalog);
        newServices.AddSingleton(newChecklist);
        newServices.AddSingleton<IOnboardingPageViewModel, WelcomePageViewModel>();
        newServices.AddSingleton<IOnboardingPageViewModel>(importPage);
        newServices.AddSingleton<IOnboardingPageViewModel, PlatformsPageViewModel>();
        await using var newProvider = newServices.BuildServiceProvider();

        wizard.CompleteContainerReload(newProvider, new LegacyImportResult
        {
            SourcePath = "old",
            TargetPath = importedDir,
            CopiedFiles = ["settings/settings.json", "settings/youtube.json"],
            SkippedFiles = [],
            Failures = [],
        });

        await wizard.NextCommand.ExecuteAsync(null);
        var currentPlatforms = wizard.CurrentPage as PlatformsPageViewModel;
        Assert.That(currentPlatforms, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(currentPlatforms, Is.Not.SameAs(oldPlatforms));
            Assert.That(currentPlatforms!.TwitchEnabled, Is.False);
            Assert.That(currentPlatforms.YouTubeEnabled, Is.True);
        });
    }

    [Test]
    public async Task CompletionPage_YouTubeOnlyPassesAndPersistsCatalogWithRollback()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json"));
        var bus = provider.GetRequiredService<IEventBus>();

        using var page = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            provider.GetRequiredService<BotConnectionManager>(),
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService(),
            ytSettingsStore,
            ytAccountsStore);

        const string channelId = "UC1234567890123456789012";
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            ytSettingsStore.Load(),
            ytAccountsStore.LoadClientSecret(),
            ytAccountsStore.LoadBot(),
            ytAccountsStore.LoadBroadcaster())
        {
            TwitchCatalog = catalog,
        };
        context.YouTubeSettings.Enabled = true;
        context.YouTubeSettings.ClientId = "yt-client";
        context.YouTubeSettings.Channel = channelId;
        context.YouTubeClientSecret = "yt-secret";
        context.YouTubeBotAccount = new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT_0000000000000001",
            ChannelTitle = "BotYT",
            AccessToken = "yt-bot-token",
            RefreshToken = "yt-bot-refresh",
            Scopes = [.. YouTubeScopes.BotRequired],
        };

        var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CompletionPageViewModel.IsValidationPending) && !page.IsValidationPending)
            {
                doneTcs.TrySetResult();
            }
        };

        page.AutoConnect = false;
        page.OnEnter(context);
        if (page.IsValidationPending)
        {
            await doneTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowTwitchChatDisplayOption, Is.False);
            Assert.That(page.SummaryText, Does.Contain("YouTube:"));
            Assert.That(page.SummaryText, Does.Not.Contain("Twitch:"));
            Assert.That(page.IsValidationFailure, Is.False);
            Assert.That(page.CanAdvance, Is.True);
        });

        Assert.That(await page.OnLeavingAsync(context), Is.True);

        var activeYouTube = catalog.GetActiveChannel(StreamingPlatform.YouTube);
        Assert.Multiple(() =>
        {
            Assert.That(settingsManager.Current.Twitch.Enabled, Is.False);
            Assert.That(ytSettingsStore.Load().Enabled, Is.True);
            Assert.That(ytSettingsStore.Load().Channel, Is.EqualTo(channelId));
            Assert.That(activeYouTube, Is.Not.Null);
            Assert.That(activeYouTube!.ChannelId, Is.EqualTo(channelId));
            Assert.That(activeYouTube.BotVerifiedSubjectId, Is.EqualTo("UC_BOT_0000000000000001"));
        });

        context.RestoreProvisionalWrites();
        Assert.That(catalog.GetActiveChannel(StreamingPlatform.YouTube), Is.Null);
    }

    [Test]
    public void HealthCheckPage_DefaultsToYouTubeAndHidesSelectorInYouTubeOnlyMode()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var page = new HealthCheckPageViewModel(
            new StubChatMessenger(),
            bus,
            NullLogger<HealthCheckPageViewModel>.Instance,
            new StubLauncher());

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false, HttpServerPort = 9090 } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "cid", Channel = "@yt" });

        page.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(page.OverlayUrl, Is.EqualTo("http://localhost:9090/chat"));
            Assert.That(page.ShowPlatformSelector, Is.False);
            Assert.That(page.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
        });
    }

    [Test]
    public async Task BotConnectionPage_TracksYouTubeJoinedAndNeutralNetworkError()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var controller = new StubConnectionController();
        var registry = new StubRegistry(
            new StubPlatform(StreamingPlatform.Twitch, "Twitch", false, false, string.Empty, StreamStatus.Offline, null),
            new StubPlatform(StreamingPlatform.YouTube, "YouTube", true, false, "yt", StreamStatus.Offline, null));

        using var page = new BotConnectionPageViewModel(
            controller,
            bus,
            settingsManager,
            accountsStore,
            NullLogger<BotConnectionPageViewModel>.Instance,
            ytSettingsStore,
            ytAccountsStore,
            registry);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "cid", Channel = "@ytchan" });

        page.OnEnter(context);
        Assert.That(page.PlatformStatuses.Select(r => r.Platform), Is.EqualTo(new[] { StreamingPlatform.YouTube }));

        await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Failed, new HttpRequestException("offline")));
        Assert.Multiple(() =>
        {
            Assert.That(page.ShowRetry, Is.True);
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.PlatformStatuses[0].DetailsText, Is.EqualTo("Ошибка сети при подключении"));
            Assert.That(page.DetailsText, Does.Not.Contain("Ошибка сети при подключении"));
            Assert.That(page.DetailsText, Does.Not.Contain("Twitch"));
        });

        page.RetryCommand.Execute(null);
        Assert.That(controller.StartedPlatforms, Is.EqualTo(new[] { StreamingPlatform.YouTube, StreamingPlatform.YouTube }));

        await bus.PublishAsync(new BotJoinedChannel(StreamingPlatform.YouTube, "@ytchan"));
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.PlatformStatuses[0].Phase, Is.EqualTo(BotLifecyclePhase.Connected));
        });
    }

    [Test]
    public async Task HttpServerCheckPage_InYouTubeOnlyModeExposesEditablePortAndRejectsInvalidPort()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var server = provider.GetRequiredService<KestrelHttpServer>();
        var page = new HttpServerCheckPageViewModel(settingsManager, server, NullLogger<HttpServerCheckPageViewModel>.Instance);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false, HttpServerPort = 0 } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true });

        page.PortText = "bad";
        page.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsPortEditable, Is.True);
            Assert.That(page.PortText, Is.EqualTo("8080"));
        });

        page.PortText = "99999";
        await page.RetryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.StatusText, Is.EqualTo("Некорректный номер порта"));
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.HasPortError, Is.True);
            Assert.That(page.IsRetryVisible, Is.False);
        });
    }

    [Test]
    public async Task HttpServerCheckPage_EditingPortResetsVerifiedStateAndShowsRetryOnlyOnServerFailure()
    {
        await using var provider = BuildProvider();
        var freePort = ReserveFreeLoopbackPort();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settingsManager.UpdateCurrent(s => s.Twitch.HttpServerPort = freePort);
        var server = provider.GetRequiredService<KestrelHttpServer>();
        var page = new HttpServerCheckPageViewModel(settingsManager, server, NullLogger<HttpServerCheckPageViewModel>.Instance);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false, HttpServerPort = freePort } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true });

        page.OnEnter(context);
        await WaitUntilCheckCompletesAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.StatusSeverity, Is.EqualTo("Success"));
            Assert.That(page.HasPortError, Is.False);
        });

        page.PortText = "99999";
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.StatusSeverity, Is.EqualTo("Error"));
            Assert.That(page.HasPortError, Is.True);
            Assert.That(page.IsRetryVisible, Is.False);
        });
        Assert.That(await page.OnLeavingAsync(context), Is.False);

        var secondFreePort = ReserveFreeLoopbackPort();
        page.PortText = secondFreePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.StatusSeverity, Is.Not.EqualTo("Success"));
            Assert.That(page.HasPortError, Is.False);
            Assert.That(page.IsRetryVisible, Is.False);
        });
        Assert.That(await page.OnLeavingAsync(context), Is.False);

        await page.RetryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.StatusSeverity, Is.EqualTo("Success"));
            Assert.That(context.Settings.Twitch.HttpServerPort, Is.EqualTo(secondFreePort));
        });
        Assert.That(await page.OnLeavingAsync(context), Is.True);

        await server.StopAsync();

        using var occupiedListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        occupiedListener.Start();
        var occupiedPort = ((System.Net.IPEndPoint)occupiedListener.LocalEndpoint).Port;

        page.PortText = occupiedPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await page.RetryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.HasPortError, Is.False);
            Assert.That(page.IsRetryVisible, Is.True);
        });
    }

    private static int ReserveFreeLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitUntilCheckCompletesAsync(HttpServerCheckPageViewModel page)
    {
        for (var attempt = 0; attempt < 50 && page.IsChecking; attempt++)
        {
            await Task.Delay(20);
        }
    }

    [Test]
    public async Task Wizard_BlocksCancelAndNavigationWhileLegacyImportPageIsBusyOrReloading()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var platforms = new PlatformsPageViewModel();

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, platforms],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        var closeRequested = false;
        wizard.CloseRequested += () => closeRequested = true;

        var source = new LegacyDataSummary
        {
            SourcePath = _tempDir,
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Files = [new("settings.json", Path.Combine(_tempDir, "settings.json"), "settings/settings.json", 10, DateTime.UtcNow)],
        };

        wizard.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [source],
            Target = null,
        });

        using var importStarted = new ManualResetEventSlim(false);
        using var allowImportFinish = new ManualResetEventSlim(false);
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReloadFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        importPage.ConfigureStartupSession(
            [source],
            null,
            async (_, _) =>
            {
                reloadStarted.TrySetResult();
                await allowReloadFinish.Task;
            },
            null,
            (src, _, _) =>
            {
                importStarted.Set();
                allowImportFinish.Wait();
                return new LegacyImportResult
                {
                    SourcePath = src,
                    TargetPath = _tempDir,
                    CopiedFiles = ["settings/settings.json"],
                    SkippedFiles = [],
                    Failures = [],
                };
            });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(importPage));
        Assert.That(wizard.CancelCommand.CanExecute(null), Is.True);

        importPage.Import!.SelectedSource = importPage.Import.Sources[0];
        var importTask = importPage.Import.ImportCommand.ExecuteAsync(null);

        Assert.That(importStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(importPage.IsBusy, Is.True);
            Assert.That(wizard.IsOperationBusy, Is.True);
            Assert.That(wizard.CancelCommand.CanExecute(null), Is.False);
            Assert.That(wizard.BackCommand.CanExecute(null), Is.False);
            Assert.That(wizard.NextCommand.CanExecute(null), Is.False);
        });

        wizard.CancelCommand.Execute(null);
        Assert.That(closeRequested, Is.False);

        allowImportFinish.Set();
        await reloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(importPage.IsReloading, Is.True);
            Assert.That(importPage.IsBusy, Is.True);
            Assert.That(wizard.IsOperationBusy, Is.True);
            Assert.That(wizard.CancelCommand.CanExecute(null), Is.False);
            Assert.That(wizard.BackCommand.CanExecute(null), Is.False);
            Assert.That(wizard.NextCommand.CanExecute(null), Is.False);
        });

        allowReloadFinish.TrySetResult();
        await importTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(importPage.IsBusy, Is.False);
            Assert.That(wizard.IsOperationBusy, Is.False);
            Assert.That(wizard.CancelCommand.CanExecute(null), Is.True);
            Assert.That(wizard.BackCommand.CanExecute(null), Is.True);
            Assert.That(wizard.NextCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task LegacyImportViewModel_PreservesResultAndHeadlineWhenAfterImportThrows()
    {
        var source = new LegacyDataSummary
        {
            SourcePath = _tempDir,
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Files = [new("settings.json", Path.Combine(_tempDir, "settings.json"), "settings/settings.json", 10, DateTime.UtcNow)],
        };

        var viewModel = new LegacyImportViewModel(
            [source],
            null,
            isSettingsEntry: false,
            new StubFilePicker(_tempDir),
            NullLogger.Instance,
            import: (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = Path.Combine(_tempDir, "target"),
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [],
            })
        {
            AfterImportAsync = (_, _) => throw new InvalidOperationException("reload failed"),
        };

        viewModel.SelectedSource = viewModel.Sources[0];
        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCompleted, Is.True);
            Assert.That(viewModel.IsBusy, Is.False);
            Assert.That(viewModel.Result, Is.Not.Null);
            Assert.That(viewModel.Result!.HasChanges, Is.True);
            Assert.That(viewModel.ResultHeadline, Is.EqualTo("Данные перенесены."));
            Assert.That(viewModel.ResultRestartNotice,
                Is.EqualTo("Данные перенесены, но обновить мастер на лету не удалось. Перезапустите приложение, чтобы применить перенесённые настройки."));
        });
    }

    [Test]
    public async Task LegacyImportPage_BlocksImportAfterProvisionalWritesAndRejectsReload()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var provisionalPage = new ProvisionalWriteStubPage(settings, accounts, ytSettingsStore, ytAccountsStore);

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, provisionalPage],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            ytSettingsStore,
            ytAccountsStore);

        var source = new LegacyDataSummary
        {
            SourcePath = _tempDir,
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Files = [new("settings.json", Path.Combine(_tempDir, "settings.json"), "settings/settings.json", 10, DateTime.UtcNow)],
        };

        var reloadInvoked = false;
        wizard.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [source],
            Target = null,
            OnImportCompletedAsync = (_, _) =>
            {
                reloadInvoked = true;
                return Task.FromResult<IServiceProvider?>(null);
            },
        });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(importPage));

        importPage.Import!.SelectedSource = importPage.Import.Sources[0];
        Assert.Multiple(() =>
        {
            Assert.That(importPage.IsImportBlockedByProvisionalWrites, Is.False);
            Assert.That(importPage.Import.PickFolderCommand.CanExecute(null), Is.True);
            Assert.That(importPage.Import.ImportCommand.CanExecute(null), Is.True);
        });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(provisionalPage));

        await wizard.NextCommand.ExecuteAsync(null);
        wizard.BackCommand.Execute(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(importPage));

        Assert.Multiple(() =>
        {
            Assert.That(importPage.IsImportBlockedByProvisionalWrites, Is.True);
            Assert.That(importPage.Import.IsBlocked, Is.True);
            Assert.That(importPage.Import.BlockedNotice,
                Is.EqualTo("Перенос недоступен после сохранения пробных настроек. Отмените мастер и откройте его снова."));
            Assert.That(importPage.Import.PickFolderCommand.CanExecute(null), Is.False);
            Assert.That(importPage.Import.ImportCommand.CanExecute(null), Is.False);
        });

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await importPage.Import.AfterImportAsync!(new LegacyImportResult
            {
                SourcePath = _tempDir,
                TargetPath = _tempDir,
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [],
            }, false));
        Assert.That(reloadInvoked, Is.False);
    }

    [Test]
    public async Task Wizard_CompleteContainerReloadDoesNotRevertImportedThemeFromOldAppearancePage()
    {
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);
        var previousTheme = ThemeManager.Current?.Key;

        try
        {
            ThemeManager.Apply(ThemeManager.DefaultLight.Key);

            await using var oldProvider = BuildProvider();
            var oldSettings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
            var oldAccounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
            var bus = oldProvider.GetRequiredService<IEventBus>();
            var controller = oldProvider.GetRequiredService<BotConnectionManager>();

            var uiStore = new MemorySettings();
            var shell = new ShellPreferences(uiStore);
            using var themeSelection = new ThemeSelectionViewModel(uiStore);
            var appearancePage = new AppearancePageViewModel(themeSelection, shell, uiStore);

            appearancePage.Appearance.Theme.Select(ThemeManager.DefaultDark.Key);
            appearancePage.Appearance.Shell.ShowPageHeader = false;

            var welcome = new WelcomePageViewModel();
            using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);

            using var wizard = new OnboardingWizardViewModel(
                [welcome, importPage, appearancePage],
                oldSettings,
                oldAccounts,
                oldProvider.GetRequiredService<KestrelHttpServer>(),
                controller,
                bus,
                NullLogger<OnboardingWizardViewModel>.Instance);

            ThemeManager.Apply(ThemeManager.DefaultDark.Key);

            var newServices = new ServiceCollection();
            newServices.AddSingleton(oldSettings);
            newServices.AddSingleton(oldAccounts);
            newServices.AddSingleton(oldProvider.GetRequiredService<KestrelHttpServer>());
            newServices.AddSingleton(controller);
            newServices.AddSingleton(bus);
            newServices.AddSingleton<IOnboardingPageViewModel, WelcomePageViewModel>();
            newServices.AddSingleton<IOnboardingPageViewModel>(importPage);
            await using var newProvider = newServices.BuildServiceProvider();

            wizard.CompleteContainerReload(newProvider, new LegacyImportResult
            {
                SourcePath = "old",
                TargetPath = _tempDir,
                CopiedFiles = ["settings/ui-preferences.toml"],
                SkippedFiles = [],
                Failures = [],
            });

            Assert.That(ThemeManager.Current?.Key, Is.EqualTo(ThemeManager.DefaultDark.Key));
        }
        finally
        {
            if (previousTheme is not null)
            {
                ThemeManager.Apply(previousTheme);
            }
        }
    }

    private sealed class ProvisionalWriteStubPage(
        SettingsManager settings,
        AccountsStore accounts,
        YouTubeSettingsStore ytSettingsStore,
        YouTubeAccountsStore ytAccountsStore) : OnboardingPageViewModelBase
    {
        public override string PageTitle => "Пробная запись";

        public override void OnEnter(OnboardingContext context)
        {
            CanAdvance = true;
        }

        public override Task<bool> OnLeavingAsync(OnboardingContext context)
        {
            context.YouTubeSettings.Enabled = true;
            context.YouTubeSettings.ClientId = "yt-provisional";
            var error = context.SaveSetup(settings, accounts, ytSettingsStore, ytAccountsStore);
            Assert.That(error, Is.Null);
            return Task.FromResult(true);
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

    private sealed class StubCredentialsValidator : IClientCredentialsValidator
    {
        public Task<ClientCredentialsValidationResult> ValidateAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
            => Task.FromResult(ClientCredentialsValidationResult.Valid);
    }

    private sealed class StubChannelValidator : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
    }

    private sealed class StubYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => Task.FromResult(new YouTubeOAuthFlowResult("a", "r", [], "c", "t", 3600));
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => Task.FromResult(new YouTubeOAuthFlowResult("a", "r", [], "c", "t", 3600));
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;
        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }

    private sealed class StubLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class StubChatMessenger : IBroadcastMessenger
    {
        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
            => ChatEnqueueResult.Queued;
        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
    }

    private sealed class StubConnectionController : IBotConnectionController
    {
        public List<StreamingPlatform> StartedPlatforms { get; } = [];
        public bool IsBusy => false;
        public BotLifecyclePhase CurrentPhase => BotLifecyclePhase.Idle;
        public BotLifecyclePhase GetPhase(StreamingPlatform platform) => BotLifecyclePhase.Idle;
        public bool IsPlatformBusy(StreamingPlatform platform) => false;
        public void StartConnection() { }
        public void StartConnection(StreamingPlatform platform) => StartedPlatforms.Add(platform);
        public void CancelConnection() { }
        public void CancelConnection(StreamingPlatform platform) { }
        public Task WaitForConnectionAsync() => Task.CompletedTask;
        public Task WaitForConnectionAsync(StreamingPlatform platform) => Task.CompletedTask;
        public Task StopAsync(BotStopMode mode = BotStopMode.Graceful) => Task.CompletedTask;
        public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful) => Task.CompletedTask;
    }

    private sealed class StubRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All { get; } = platforms;
        public bool TryGet(StreamingPlatform platform, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = Find(platform);
            return chatPlatform is not null;
        }
        public IChatPlatform? Find(StreamingPlatform platform) => All.FirstOrDefault(p => p.Id == platform);
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => All.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => All.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform(
        StreamingPlatform id,
        string displayName,
        bool isEnabled,
        bool isConnected,
        string channel,
        StreamStatus status,
        string? title) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled;
        public bool IsConnected => isConnected;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus(status, title is null ? null : new StreamInfo { Title = title });
        public IPlatformMessenger Messenger => null!;
        public string? ConfiguredChannel => channel;
        public string? BuildStreamUrl(StreamInfo? stream = null) => $"https://example.invalid/{channel}";
    }

    private sealed class StubStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;
        public StreamInfo? CurrentStream => info;
        public StreamStatusSnapshot GetSnapshot() => new(status, info, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
