using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeepShell.Bootstrap;
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
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.Views.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class LegacyImportPageViewModelTests
{
    private string _tempDir = string.Empty;

    [OneTimeSetUp]
    public void RegisterPackUris() => PackScheme.Ensure();

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"legacy-import-page-{Guid.NewGuid():N}");
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
    public async Task R3_CopyException_ShowsErrorPanelAndBlocksAdvanceWhileAllowingRetryAndCancel()
    {
        var context = CreateContext();
        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var source = CreateSource(_tempDir);

        page.ConfigureStartupSession(
            [source],
            null,
            null,
            null,
            (_, _, _) => throw new IOException("copy interrupted"));

        page.OnEnter(context);
        Assert.That(page.NextButtonText, Is.EqualTo("Продолжить без переноса"));

        page.Import!.SelectedSource = page.Import.Sources[0];
        await page.Import.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.HasCompletedImport, Is.True, "Панель результата/ошибки должна быть видимой даже при Result == null");
            Assert.That(page.Import.Result, Is.Null);
            Assert.That(page.IsImportInterrupted, Is.True);
            Assert.That(page.HasReadinessSummary, Is.False);
            Assert.That(page.Import.ResultHeadline, Is.EqualTo("Перенос прервался"));
            Assert.That(page.CanAdvance, Is.False, "«Далее» не должно оставаться доступным после обрыва копирования");
            Assert.That(page.Import.ResetAfterInterruptionCommand.CanExecute(null), Is.True);
        }

        page.Import.ResetAfterInterruptionCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.HasCompletedImport, Is.False);
            Assert.That(page.IsImportInterrupted, Is.False);
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.Import.PickFolderCommand.CanExecute(null), Is.True);
            Assert.That(page.Import.ImportCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public async Task R4_ReloadFailure_HidesStaleChecklistAndBlocksAdvanceWithRestartRecovery()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var checklist = new OnboardingChecklist(settings, accounts);
        var context = new OnboardingContext(settings.Current, new(), new())
        {
            IncludeLegacyImport = true,
            Checklist = checklist,
        };

        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var source = CreateSource(_tempDir);

        page.ConfigureStartupSession(
            [source],
            null,
            (_, _) => throw new InvalidOperationException("container reload failed"),
            null,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = ["settings/settings.json", "settings/accounts.json"],
                SkippedFiles = [],
                Failures = [],
                BotAuthorizationRequired = true,
            });

        page.OnEnter(context);
        page.Import!.SelectedSource = page.Import.Sources[0];
        await page.Import.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.HasCompletedImport, Is.True);
            Assert.That(page.IsImportReloadFailed, Is.True);
            Assert.That(page.HasReadinessSummary, Is.False);
            Assert.That(page.MissingItems, Is.Empty, "Старый OnboardingChecklist не должен оцениваться при сбое перечитывания");
            Assert.That(page.Import.ResultAuthorizationNotice, Is.Null, "Не должно требовать заново ввести скопированные реквизиты");
            Assert.That(page.Import.ResultRestartNotice, Does.Contain("Перезапустите приложение"));
            Assert.That(page.CanAdvance, Is.False);
        }
    }

    [Test]
    public async Task R13_YouTubeOnlyImport_DoesNotPromptTwitchLogin()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings { Twitch = new TwitchSettings { Enabled = false } });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        ytSettings.Save(new YouTubeSettings
        {
            Enabled = true,
            ClientId = "yt-cid",
            Channel = "UC1234567890123456789012",
        });
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        ytAccounts.SaveAll(
            string.Empty,
            new YouTubeAccountSettings
            {
                ChannelId = "UC_BOT_0000000000000001",
                AccessToken = "yt-bot",
                RefreshToken = "yt-ref",
                Scopes = [.. YouTubeScopes.BotRequired],
            },
            new());

        var checklist = new OnboardingChecklist(settings, accounts, youtubeSettings: ytSettings, youtubeAccounts: ytAccounts);
        var context = new OnboardingContext(
            settings.Current,
            new(),
            new(),
            ytSettings.Load(),
            string.Empty,
            ytAccounts.LoadBot(),
            ytAccounts.LoadBroadcaster())
        {
            IncludeLegacyImport = true,
            Checklist = checklist,
        };

        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var source = CreateSource(_tempDir);
        page.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            null,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = ["settings/settings.json", "settings/youtube.json"],
                SkippedFiles = [],
                Failures = [],
                BotAuthorizationRequired = true,
            });

        page.OnEnter(context);
        page.Import!.SelectedSource = page.Import.Sources[0];
        await page.Import.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.IsProfileComplete, Is.True);
            Assert.That(page.Import.ResultAuthorizationNotice, Is.Null);
            Assert.That(page.NextButtonText, Is.EqualTo("Открыть панель"));
        }
    }

    [Test]
    public async Task R14_And_R15_WizardRoutesToFinishOrMissingStepOrAppearance()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings
            {
                Enabled = true,
                ClientId = "cid",
                ClientSecret = "sec",
                Channel = "streamer",
            },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var checklist = new OnboardingChecklist(settings, accounts);
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = provider.GetRequiredService<BotConnectionManager>();

        var welcome = new WelcomePageViewModel();
        using var importPage = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var uiStore = new MemorySettings();
        using var theme = new ThemeSelectionViewModel(uiStore);
        using var appearance = new AppearancePageViewModel(theme, new ShellPreferences(uiStore), uiStore);
        var platforms = new PlatformsPageViewModel();
        using var credentials = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());
        using var botAuth = new BotAuthorizationPageViewModel(
            new StubTwitchOAuthService(),
            accounts,
            NullLogger<BotAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new StubClipboardService(),
            new StubEmbeddedAuthDialog(),
            new ManualUiDispatcher());

        using var wizard = new OnboardingWizardViewModel(
            [welcome, importPage, appearance, platforms, credentials, botAuth],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            controller,
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            checklist: checklist);

        var source = CreateSource(_tempDir);
        wizard.AttachStartupSession(new StartupOnboardingSession
        {
            IncludeLegacyImport = true,
            Candidates = [source],
            Target = null,
        });

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
                BotAuthorizationRequired = true,
            });

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(importPage));
        Assert.That(wizard.NextButtonText, Is.EqualTo("Продолжить без переноса"));

        importPage.Import!.SelectedSource = importPage.Import.Sources[0];
        await importPage.Import.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(importPage.IsProfileComplete, Is.False);
            Assert.That(importPage.FirstMissingStep, Is.EqualTo(OnboardingStep.BotAuthorization));
            Assert.That(importPage.HasMissingStep, Is.True);
            Assert.That(wizard.NextButtonText, Is.EqualTo("Перейти ко входу бота"));
            Assert.That(importPage.ReviewSettingsButtonText, Is.EqualTo("Проверить всё с оформления"));
        }

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(botAuth), "Основной переход после частичного переноса должен вести к первому недостающему шагу");

        wizard.StartAt(OnboardingStep.LegacyImport);
        importPage.ReviewSettingsCommand.Execute(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(appearance), "Вторичный переход должен открывать проверку с шага оформления");
    }

    [Test]
    public async Task R16_RepeatImportWithZeroCopiedFiles_PreservesCompleteReadinessWhenWorkingProfileIsComplete()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec", Channel = "streamer" },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        accounts.SaveAll(new TwitchAccountSettings
        {
            AccessToken = "bot",
            RefreshToken = "ref",
            StoredScopes = [.. TwitchScopes.BotRequired],
        }, new());
        var checklist = new OnboardingChecklist(settings, accounts);
        var context = new OnboardingContext(settings.Current, new(), new())
        {
            IncludeLegacyImport = true,
            Checklist = checklist,
        };

        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var source = CreateSource(_tempDir);
        page.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            null,
            (src, _, _) => new LegacyImportResult
            {
                SourcePath = src,
                TargetPath = _tempDir,
                CopiedFiles = [],
                SkippedFiles = [new("settings/settings.json", LegacyImportSkipReason.TargetExists)],
                Failures = [],
            });

        page.OnEnter(context);
        page.Import!.SelectedSource = page.Import.Sources[0];
        await page.Import.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Import.Result!.HasChanges, Is.False);
            Assert.That(page.IsProfileComplete, Is.True);
            Assert.That(page.FinishImmediatelyCommand.CanExecute(null), Is.True);
            Assert.That(page.NextButtonText, Is.EqualTo("Открыть панель"));
            Assert.That(page.ReviewSettingsButtonText, Is.EqualTo("Проверить настройки"));
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void R36_And_R14_ViewKeepsPinnedFooterAndInterceptsEnterOnSourceList()
    {
        using var page = new LegacyImportPageViewModel(new StubFilePicker(_tempDir), NullLogger<LegacyImportPageViewModel>.Instance);
        var source = CreateSource(_tempDir);
        var importCalls = 0;

        page.ConfigureStartupSession(
            [source],
            null,
            (_, _) => Task.CompletedTask,
            null,
            (src, _, _) =>
            {
                importCalls++;
                return new LegacyImportResult
                {
                    SourcePath = src,
                    TargetPath = _tempDir,
                    CopiedFiles = [],
                    SkippedFiles = [],
                    Failures = [],
                };
            });

        page.OnEnter(CreateContext());
        page.Import!.SelectedSource = page.Import.Sources[0];

        var view = new LegacyImportPageView { DataContext = page };
        view.Measure(new Size(688, 420));
        view.Arrange(new Rect(0, 0, 688, 420));
        view.UpdateLayout();

        var listBox = FindVisualChild<ListBox>(view);
        var buttons = FindVisualChildren<Button>(view).ToList();
        var checkBoxes = FindVisualChildren<CheckBox>(view).ToList();
        var pickButton = buttons.First(b => Equals(b.Content, "Выбрать папку…"));
        var importButton = buttons.First(b => Equals(b.Content, "Перенести"));
        var neverAskCheckBox = checkBoxes.First(c => Equals(c.Content, "Больше не предлагать перенос при запуске"));

        Assert.That(listBox, Is.Not.Null);

        var pickPos = pickButton.TranslatePoint(new Point(0, 0), view);
        var listPos = listBox!.TranslatePoint(new Point(0, 0), view);
        var importPos = importButton.TranslatePoint(new Point(0, 0), view);
        var neverAskPos = neverAskCheckBox.TranslatePoint(new Point(0, 0), view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listPos.Y, Is.GreaterThan(pickPos.Y));
            Assert.That(importPos.Y, Is.GreaterThan(listPos.Y), "Кнопка «Перенести» должна быть закреплена под списком источников");
            Assert.That(neverAskPos.Y, Is.GreaterThan(importPos.Y), "Флажок «Больше не предлагать» не должен идти перед «Перенести» в порядке Tab");
        }

        var presentationSource = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("test"));
        try
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, Key.Enter)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent,
            };
            listBox.RaiseEvent(args);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(importCalls, Is.EqualTo(1));
            }
        }
        finally
        {
            presentationSource.Dispose();
        }
    }

    private OnboardingContext CreateContext() =>
        new(new AppSettings(), new(), new()) { IncludeLegacyImport = true };

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

    private sealed class StubTwitchOAuthService : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => Task.FromException<OAuthFlowResult>(new NotSupportedException());
        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public bool TrySetText(string? text) => true;
    }

    private sealed class StubLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubEmbeddedAuthDialog : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(EmbeddedTwitchAuthResult.Canceled("canceled"));
    }
}
