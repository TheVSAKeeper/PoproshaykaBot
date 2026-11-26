using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using KeepShell.Views.Controls;
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
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class TwitchOnboardingStage4Tests
{
    private string _tempDir = string.Empty;

    [OneTimeSetUp]
    public void RegisterPackUris() => PackScheme.Ensure();

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stage4-twitch-{Guid.NewGuid():N}");
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
    public void CredentialsPage_DefaultRedirectUriIs8080_InstructionAndCopyUseActualUri()
    {
        var clipboard = new RecordingClipboard();
        using var vm = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher(),
            clipboard: clipboard);

        var context = new OnboardingContext(new AppSettings { Twitch = new TwitchSettings { RedirectUri = string.Empty } }, new(), new());
        vm.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(vm.RedirectUri, Is.EqualTo("http://localhost:8080"));
            Assert.That(vm.EffectiveRedirectUri, Is.EqualTo("http://localhost:8080"));
            Assert.That(vm.InstructionText, Does.Contain("http://localhost:8080"));
            Assert.That(vm.InstructionText, Does.Not.Contain("3000"));
        });

        vm.RedirectUri = "http://localhost:9123/callback";
        vm.CopyRedirectUriCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.EffectiveRedirectUri, Is.EqualTo("http://localhost:9123/callback"));
            Assert.That(vm.InstructionText, Does.Contain("http://localhost:9123/callback"));
            Assert.That(clipboard.LastCopied, Is.EqualTo("http://localhost:9123/callback"));
            Assert.That(vm.RedirectUriCopyFeedback, Does.Contain("http://localhost:9123/callback"));
        });
    }

    [Test]
    public async Task CredentialsPage_BackNavigationPreservesUnfinishedDraftInContext()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var platforms = new PlatformsPageViewModel();
        using var credentials = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());

        using var wizard = CreateWizard(provider, settings, accounts, [platforms, credentials]);
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(credentials));

        credentials.AutoDetectChannel = false;
        credentials.Channel = "review_draft";
        credentials.ClientId = "cid-draft";
        credentials.ClientSecret = "sec-draft";
        credentials.RedirectUri = "http://localhost:9001";

        wizard.BackCommand.Execute(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(platforms));

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(credentials));

        Assert.Multiple(() =>
        {
            Assert.That(credentials.AutoDetectChannel, Is.False);
            Assert.That(credentials.Channel, Is.EqualTo("review_draft"));
            Assert.That(credentials.ClientId, Is.EqualTo("cid-draft"));
            Assert.That(credentials.ClientSecret, Is.EqualTo("sec-draft"));
            Assert.That(credentials.RedirectUri, Is.EqualTo("http://localhost:9001"));
        });
    }

    [Test]
    public async Task CredentialsPage_InvalidCredentialsBlocksAdvance_WhileNetworkErrorAllowsAdvance()
    {
        var dispatcher = new ManualUiDispatcher();
        var validator = new ScriptedCredentialsValidator();
        using var vm = new CredentialsPageViewModel(
            validator,
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            dispatcher);

        vm.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));
        vm.ClientId = "bad-id";
        vm.ClientSecret = "bad-sec";
        Assert.That(vm.CanAdvance, Is.True);

        validator.NextResult = ClientCredentialsValidationResult.Invalid;
        dispatcher.Timers[0].Tick();
        await validator.WaitCompletedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(vm.ClientStatusText, Does.Not.Contain("✓").And.Not.Contain("✗"));
            Assert.That(vm.CanAdvance, Is.False);
            Assert.That(vm.HasValidationError, Is.True);
        });

        validator.ResetCompletion();
        validator.NextResult = ClientCredentialsValidationResult.NetworkError;
        vm.ClientId = "maybe-id";
        Assert.That(vm.CanAdvance, Is.True);

        dispatcher.Timers[0].Tick();
        await validator.WaitCompletedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(vm.CanAdvance, Is.True);
            Assert.That(vm.CanRetryClientCheck, Is.True);
        });
    }

    [Test]
    public async Task CredentialsPage_ValidationTimeoutSetsWarningAndAllowsRetry()
    {
        var dispatcher = new ManualUiDispatcher();
        var credentialsValidator = new HangingCredentialsValidator();
        var channelValidator = new HangingChannelValidator();
        using var vm = new CredentialsPageViewModel(
            credentialsValidator,
            channelValidator,
            NullLogger<CredentialsPageViewModel>.Instance,
            dispatcher)
        {
            ClientValidationTimeout = TimeSpan.FromMilliseconds(20),
            ChannelValidationTimeout = TimeSpan.FromMilliseconds(20),
        };

        vm.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));
        vm.AutoDetectChannel = false;
        vm.Channel = "some_channel";
        vm.ClientId = "cid";
        vm.ClientSecret = "sec";

        dispatcher.Timers[0].Tick();
        dispatcher.Timers[1].Tick();

        await credentialsValidator.WaitFinishedAsync();
        await channelValidator.WaitFinishedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusText, Is.EqualTo("Превышено время ожидания проверки"));
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(vm.CanRetryClientCheck, Is.True);
            Assert.That(vm.ChannelStatusText, Is.EqualTo("Превышено время ожидания проверки"));
            Assert.That(vm.ChannelStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(vm.CanRetryChannelCheck, Is.True);
            Assert.That(vm.CanAdvance, Is.True);
        });

        credentialsValidator.ShouldHang = false;
        channelValidator.ShouldHang = false;
        await vm.RetryClientValidationCommand.ExecuteAsync(null);
        await vm.RetryChannelValidationCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(vm.CanRetryClientCheck, Is.False);
            Assert.That(vm.ChannelStatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(vm.CanRetryChannelCheck, Is.False);
        });
    }

    [Test]
    public async Task CredentialsPage_TogglingAutoDetectAndClearingFieldsUpdatesChannelStatus()
    {
        var dispatcher = new ManualUiDispatcher();
        var channelValidator = new ScriptedChannelValidator();
        using var vm = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            channelValidator,
            NullLogger<CredentialsPageViewModel>.Instance,
            dispatcher);

        vm.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));
        Assert.That(vm.ChannelStatusText, Does.Contain("автоматически"));

        vm.AutoDetectChannel = false;
        Assert.Multiple(() =>
        {
            Assert.That(vm.ChannelStatusText, Is.Empty);
            Assert.That(vm.ChannelStatusSeverity, Is.EqualTo(StatusSeverity.None));
        });

        vm.Channel = "target_chan";
        vm.ClientId = "cid";
        vm.ClientSecret = "sec";
        dispatcher.Timers[1].Tick();
        await channelValidator.WaitCompletedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ChannelStatusText, Is.EqualTo("Канал @target_chan найден на Twitch"));
            Assert.That(vm.ChannelStatusSeverity, Is.EqualTo(StatusSeverity.Success));
        });

        vm.Channel = string.Empty;
        Assert.Multiple(() =>
        {
            Assert.That(vm.ChannelStatusText, Is.Empty);
            Assert.That(vm.ChannelStatusSeverity, Is.EqualTo(StatusSeverity.None));
        });

        vm.AutoDetectChannel = true;
        Assert.That(vm.ChannelStatusText, Does.Contain("автоматически"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CredentialsPageView_HasAutomationNamesAndTopValidationBannerWithoutScrolling()
    {
        TestResources.Ensure();
        AppScaledSizes.Register(Application.Current!);

        using var vm = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());
        vm.OnEnter(new OnboardingContext(new AppSettings { Twitch = new TwitchSettings { ClientId = string.Empty, ClientSecret = string.Empty } }, new(), new()));

        var view = new CredentialsPageView { DataContext = vm };
        var window = new Window { WindowStyle = WindowStyle.None, Content = view };
        using var host = VisualTestHost.Show(window, 640, 480);
        host.Settle();

        var textBoxes = FindVisualChildren<TextBox>(view).ToList();
        var passwordBoxes = FindVisualChildren<PasswordBox>(view).ToList();
        var revealables = FindVisualChildren<RevealableTextBox>(view).ToList();
        var scrollViewer = FindVisualChildren<ScrollViewer>(view).First();
        var validationText = FindVisualChildren<TextBlock>(view).First(tb => tb.Text.StartsWith("Заполните:", StringComparison.Ordinal));
        var validationTop = validationText.TranslatePoint(new Point(0, 0), view).Y;
        var scrollTop = scrollViewer.TranslatePoint(new Point(0, 0), view).Y;

        Assert.Multiple(() =>
        {
            Assert.That(validationTop, Is.LessThan(scrollTop), "Причина блокировки должна быть выше ScrollViewer и видна без прокрутки.");
            Assert.That(textBoxes.Select(AutomationProperties.GetName), Does.Contain("Канал Twitch"));
            Assert.That(textBoxes.Select(AutomationProperties.GetName), Does.Contain("Redirect URI"));
            Assert.That(revealables.Select(AutomationProperties.GetName), Does.Contain("Client ID"));
            Assert.That(revealables.Select(AutomationProperties.GetName), Does.Contain("Client Secret"));
            Assert.That(passwordBoxes.Select(AutomationProperties.GetName), Does.Contain("Client ID"));
            Assert.That(passwordBoxes.Select(AutomationProperties.GetName), Does.Contain("Client Secret"));
        });

        foreach (var revealable in revealables)
        {
            revealable.IsRevealed = true;
        }

        host.Settle();
        var revealedTextBoxes = FindVisualChildren<TextBox>(view).Select(AutomationProperties.GetName).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(revealedTextBoxes, Does.Contain("Client ID"));
            Assert.That(revealedTextBoxes, Does.Contain("Client Secret"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task AuthorizationPageView_WrapsButtonsAndPlacesCancelAuthNextToStatus()
    {
        TestResources.Ensure();
        AppScaledSizes.Register(Application.Current!);

        var oauth = new HangingTwitchOAuthService();
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        using var vm = new BotAuthorizationPageViewModel(
            oauth,
            accounts,
            NullLogger<BotAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());
        vm.OnEnter(new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" } },
            new(),
            new()));

        var authTask = vm.OpenBrowserCommand.ExecuteAsync(null);

        var view = new AuthorizationPageView { DataContext = vm };
        var window = new Window { WindowStyle = WindowStyle.None, Content = view };
        using var host = VisualTestHost.Show(window, 640, 480);
        host.Settle();

        var cancelBtn = FindVisualChildren<Button>(view).Single(b => Equals(b.Content, "Отменить вход"));
        var copyBtn = FindVisualChildren<Button>(view).Single(b => Equals(b.Content, "Скопировать ссылку"));

        Assert.Multiple(() =>
        {
            Assert.That(cancelBtn.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(FindVisualChildren<WrapPanel>(view).Count(), Is.GreaterThanOrEqualTo(2));
            Assert.That(copyBtn.TranslatePoint(new Point(copyBtn.ActualWidth, 0), view).X, Is.LessThanOrEqualTo(640));
            Assert.That(cancelBtn.TranslatePoint(new Point(cancelBtn.ActualWidth, 0), view).X, Is.LessThanOrEqualTo(640));
        });

        vm.TryHandleEscape();
        await authTask;
    }

    [Test]
    public async Task AuthorizationPage_FixedChannelAssignment_ShowsExpectedAccountAndExplainsRejection()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "bot-ctx", "expected_bot", new()
        {
            UserId = "1",
            Login = "expected_bot",
            AccessToken = "at",
            RefreshToken = "rt",
            StoredScopes = [.. TwitchScopes.BotRequired],
        });
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new ChannelCatalogEntry
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "42",
                    Handle = "streamchan",
                    IsConfigured = true,
                    BotAccountId = "1",
                    BotContextId = "bot-ctx",
                    BotVerifiedSubjectId = "1",
                },
            ],
        });

        var oauth = new ScriptedTwitchOAuthService(new OAuthFlowResult("at2", "rt2", [.. TwitchScopes.BotRequired], "wrong_bot", "2", 3600));
        using var vm = new BotAuthorizationPageViewModel(
            oauth,
            accounts,
            NullLogger<BotAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());

        var botAccount = accounts.Load(PlatformAuthRole.Bot, "bot-ctx");
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" } },
            botAccount,
            new())
        {
            TwitchCatalog = catalog,
            TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
        };
        context.CaptureTwitchAccounts(botAccount, null);

        vm.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAccountFixed, Is.True);
            Assert.That(vm.ExpectedAccountLabel, Is.EqualTo("@expected_bot"));
            Assert.That(vm.ExpectedAccountHint, Does.Contain("@expected_bot").And.Contain("Настройки -> Площадки и каналы"));
            Assert.That(vm.EmbeddedAuthButtonText, Is.EqualTo("Обновить вход в приложении"));
            Assert.That(vm.OpenBrowserButtonText, Is.EqualTo("Обновить через браузер"));
            Assert.That(vm.StatusMessage, Does.Contain("@expected_bot"));
        });

        await vm.OpenBrowserCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StatusMessageSeverity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(vm.StatusMessage, Does.Contain("@wrong_bot"));
            Assert.That(vm.StatusMessage, Does.Contain("@expected_bot"));
            Assert.That(vm.StatusMessage, Does.Contain("Настройки -> Площадки и каналы"));
        });
    }

    [Test]
    public async Task AuthorizationPage_LeavingOrNavigatingBackCancelsPendingBrowserAuth()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var oauth = new HangingTwitchOAuthService();
        var platforms = new PlatformsPageViewModel();
        using var botAuth = new BotAuthorizationPageViewModel(
            oauth,
            accounts,
            NullLogger<BotAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());

        using var wizard = CreateWizard(provider, settings, accounts, [platforms, botAuth]);
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(botAuth));

        var authTask = botAuth.OpenBrowserCommand.ExecuteAsync(null);
        Assert.That(botAuth.IsAuthInProgress, Is.True);

        wizard.BackCommand.Execute(null);
        await authTask;

        Assert.Multiple(() =>
        {
            Assert.That(wizard.CurrentPage, Is.SameAs(platforms));
            Assert.That(oauth.WasCanceled, Is.True);
            Assert.That(botAuth.IsAuthInProgress, Is.False);
        });
    }

    [Test]
    public async Task BroadcasterAuthorization_OptionalSkipUsesClearNextButtonTextAndEscapeCancelsAuthFirst()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" },
        });
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec", Channel = "mychan" },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var oauth = new HangingTwitchOAuthService();

        using var broadcaster = new BroadcasterAuthorizationPageViewModel(
            oauth,
            accounts,
            NullLogger<BroadcasterAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());
        var completion = new PlatformsPageViewModel();

        using var wizard = CreateWizard(provider, settings, accounts, [broadcaster, completion]);
        var closeCount = 0;
        wizard.CloseRequested += () => closeCount++;

        Assert.Multiple(() =>
        {
            Assert.That(broadcaster.CanAdvance, Is.True);
            Assert.That(broadcaster.NextButtonTextOverride, Is.EqualTo("Продолжить только с ботом"));
            Assert.That(broadcaster.IsNextActionSecondary, Is.True);
            Assert.That(wizard.NextButtonText, Is.EqualTo("Продолжить только с ботом"));
            Assert.That(wizard.IsNextActionSecondary, Is.True);
            Assert.That(broadcaster.BroadcasterCapabilitiesHint, Does.Contain("опросы").And.Contain("управление трансляцией"));
        });

        var authTask = broadcaster.OpenBrowserCommand.ExecuteAsync(null);
        Assert.That(broadcaster.IsAuthInProgress, Is.True);

        wizard.HandleEscapeCommand.Execute(null);
        await authTask;

        Assert.Multiple(() =>
        {
            Assert.That(oauth.WasCanceled, Is.True);
            Assert.That(closeCount, Is.Zero, "Escape при активном OAuth должен отменять только вход, а не весь мастер.");
            Assert.That(broadcaster.IsAuthInProgress, Is.False);
        });

        wizard.HandleEscapeCommand.Execute(null);
        Assert.That(closeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SwitchToBrowserFromEmbeddedDialog_ImmediatelyStartsBrowserFlowInWizardAndSettings()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var twitchOAuth = new ScriptedTwitchOAuthService(
            new OAuthFlowResult("tw-at", "tw-rt", [.. TwitchScopes.BotRequired], "botlogin", "101", 3600));
        var embeddedTwitch = new StubEmbeddedTwitchAuthDialog(
            EmbeddedTwitchAuthResult.SwitchToBrowser("Открываем вход в Twitch через системный браузер..."));

        using var wizardPage = new BotAuthorizationPageViewModel(
            twitchOAuth,
            accounts,
            NullLogger<BotAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            embeddedTwitch,
            new ManualUiDispatcher());
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec" } },
            new(),
            new());
        wizardPage.OnEnter(context);

        await wizardPage.AuthorizeInAppCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(twitchOAuth.BrowserFlowCount, Is.EqualTo(1));
            Assert.That(wizardPage.HasToken, Is.True);
            Assert.That(context.BotAccount.Login, Is.EqualTo("botlogin"));
        });

        var adapter = new TwitchAccountAuthAdapter(
            PlatformAuthRole.Bot,
            twitchOAuth,
            accounts,
            () => new OAuthCredentialsSnapshot("cid", "sec", "http://localhost:8080", "chan"),
            embeddedTwitch);
        using var settingsVm = new PlatformOAuthAccountViewModel(
            adapter,
            new StubDialogService(),
            new StubLauncher(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLogger.Instance);

        await settingsVm.AuthorizeInAppCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(twitchOAuth.BrowserFlowCount, Is.EqualTo(2));
            Assert.That(settingsVm.HasToken, Is.True);
            Assert.That(settingsVm.LoginText, Is.EqualTo("botlogin"));
        });
    }

    [Test]
    public async Task BroadcasterAuthorization_AfterAuthClearsSecondaryNextAction()
    {
        await using var provider = BuildProvider();
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { ClientId = "cid", ClientSecret = "sec", Channel = "mychan" },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var oauth = new ScriptedTwitchOAuthService(new OAuthFlowResult("at", "rt", [.. TwitchScopes.BroadcasterRequired], "mychan", "42", 3600));

        using var broadcaster = new BroadcasterAuthorizationPageViewModel(
            oauth,
            accounts,
            NullLogger<BroadcasterAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());
        using var wizard = CreateWizard(provider, settings, accounts, [broadcaster]);

        Assert.That(wizard.IsNextActionSecondary, Is.True);
        await broadcaster.OpenBrowserCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(broadcaster.HasToken, Is.True);
            Assert.That(broadcaster.IsNextActionSecondary, Is.False);
            Assert.That(wizard.IsNextActionSecondary, Is.False);
        });
    }

    [Test]
    public async Task CredentialsPage_RevertingToPreviouslyValidatedCredentialsBeforeTimerTick_RestoresCachedStatus()
    {
        var dispatcher = new ManualUiDispatcher();
        var validator = new ScriptedCredentialsValidator();
        using var vm = new CredentialsPageViewModel(
            validator,
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            dispatcher);

        vm.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));
        vm.ClientId = "valid-id";
        vm.ClientSecret = "valid-sec";
        dispatcher.Timers[0].Tick();
        await validator.WaitCompletedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(vm.ClientStatusText, Is.EqualTo("Client ID и Secret действительны"));
        });

        vm.ClientId = "edited-id";
        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusText, Is.EqualTo("Ожидание ввода..."));
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.None));
        });

        vm.ClientId = "valid-id";
        Assert.Multiple(() =>
        {
            Assert.That(vm.ClientStatusText, Is.EqualTo("Client ID и Secret действительны"));
            Assert.That(vm.ClientStatusSeverity, Is.EqualTo(StatusSeverity.Success));
        });
    }

    [Test]
    public async Task Wizard_OpeningCredentialsWithEmptySavedRedirectUri_DoesNotTriggerUnsavedPromptOnClose()
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
                Channel = "mychan",
                RedirectUri = string.Empty,
            },
        });
        var accounts = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var dialogs = new CountingDialogService();
        var platforms = new PlatformsPageViewModel();
        using var credentials = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());

        using var wizard = new OnboardingWizardViewModel(
            [platforms, credentials],
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            provider.GetRequiredService<BotConnectionManager>(),
            provider.GetRequiredService<IEventBus>(),
            NullLogger<OnboardingWizardViewModel>.Instance,
            dialogService: dialogs);

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(wizard.CurrentPage, Is.SameAs(credentials));
            Assert.That(credentials.RedirectUri, Is.EqualTo(CredentialsPageViewModel.DefaultRedirectUri));
        });

        Assert.That(await wizard.RequestCloseAsync(confirmIfModified: true), Is.True);
        Assert.That(dialogs.ConfirmWarningCalls, Is.Zero);
    }

    private static OnboardingWizardViewModel CreateWizard(
        ServiceProvider provider,
        SettingsManager settings,
        AccountsStore accounts,
        IEnumerable<IOnboardingPageViewModel> pages) =>
        new(pages,
            settings,
            accounts,
            provider.GetRequiredService<KestrelHttpServer>(),
            provider.GetRequiredService<BotConnectionManager>(),
            provider.GetRequiredService<IEventBus>(),
            NullLogger<OnboardingWizardViewModel>.Instance);

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
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? LastCopied { get; private set; }
        public bool TrySetText(string? text)
        {
            LastCopied = text;
            return true;
        }
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

    private sealed class CountingDialogService : IDialogService
    {
        public int ConfirmWarningCalls { get; private set; }
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false)
        {
            ConfirmWarningCalls++;
            return true;
        }
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
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

    private sealed class ScriptedCredentialsValidator : IClientCredentialsValidator
    {
        private TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ClientCredentialsValidationResult NextResult { get; set; } = ClientCredentialsValidationResult.Valid;

        public void ResetCompletion() => _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitCompletedAsync() => _completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task<ClientCredentialsValidationResult> ValidateAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            _completed.TrySetResult();
            return Task.FromResult(NextResult);
        }
    }

    private sealed class ScriptedChannelValidator : IOnboardingChannelValidator
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitCompletedAsync() => _completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            _completed.TrySetResult();
            return Task.FromResult(ChannelValidationResult.Found);
        }
    }

    private sealed class HangingCredentialsValidator : IClientCredentialsValidator
    {
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShouldHang { get; set; } = true;
        public Task WaitFinishedAsync() => _finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<ClientCredentialsValidationResult> ValidateAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            if (ShouldHang)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    _finished.TrySetResult();
                }
            }

            return ClientCredentialsValidationResult.Valid;
        }
    }

    private sealed class HangingChannelValidator : IOnboardingChannelValidator
    {
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShouldHang { get; set; } = true;
        public Task WaitFinishedAsync() => _finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);

        public async Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            if (ShouldHang)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    _finished.TrySetResult();
                }
            }

            return ChannelValidationResult.Found;
        }
    }

    private sealed class StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult result) : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class HangingTwitchOAuthService : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public bool WasCanceled { get; private set; }

        public async Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            string? redirectUri,
            Action<string> onAuthUrlReady,
            bool checkBroadcasterChannel = true,
            CancellationToken ct = default)
        {
            onAuthUrlReady("https://id.twitch.tv/oauth2/authorize?state=hang");
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }

            return new("a", "r", [], "u", "1", 3600);
        }

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => throw new NotSupportedException();
        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult("at");
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class ScriptedTwitchOAuthService(OAuthFlowResult result) : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public int BrowserFlowCount { get; private set; }

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            string? redirectUri,
            Action<string> onAuthUrlReady,
            bool checkBroadcasterChannel = true,
            CancellationToken ct = default)
        {
            BrowserFlowCount++;
            onAuthUrlReady("https://id.twitch.tv/oauth2/authorize?state=ok");
            return Task.FromResult(result);
        }

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => throw new NotSupportedException();
        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult("at");
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }
}
