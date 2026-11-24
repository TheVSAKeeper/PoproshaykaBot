using System.IO;
using System.Reflection;
using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using KeepShell.Views;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views.Dialogs;

namespace PoproshaykaBot.Wpf.Tests.Dialogs;

[TestFixture]
public sealed class MultistreamWizardDialogsTests
{
    private static readonly string[] ViewDictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Dark.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
    ];

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-ms-wizards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task PlatformSetupWizard_enables_youtube_immediately_and_verifies_without_retry_button_on_success()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"));

        ytSettings.Save(new() { Enabled = false, ClientId = "yt-client" });
        ytAccounts.SaveAll(
            "yt-secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref" },
            new YouTubeAccountSettings());
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_MAIN",
                    DisplayName = "@MainChannel",
                    IsConfigured = true,
                    BotAccountId = "UC_BOT",
                    BotContextId = AuthContextIds.Default,
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_MAIN" }],
        });

        var adapter = new StubChannelAdapter(StreamingPlatform.YouTube, () => ytAccounts.ListAccounts())
        {
            VerifyHandler = (_, _, _) => new(true, "UC_BOT"),
        };

        var vm = new MultistreamPlatformSetupDialogViewModel(
            catalog, [adapter], twitchAccounts, ytAccounts, ytSettings);
        vm.Initialize(StreamingPlatform.YouTube);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(1));
            Assert.That(vm.StepProgressText, Is.EqualTo("Шаг 1 из 3"));
            Assert.That(vm.EnableYouTube, Is.False);
            Assert.That(vm.ShowQuickEnableYouTubeButton, Is.True);
        });

        await vm.EnableYouTubeAndContinueCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(ytSettings.Load().Enabled, Is.True);
            Assert.That(vm.HasSavedChanges, Is.True);
            Assert.That(vm.CurrentStep, Is.EqualTo(2));
            Assert.That(vm.HasExistingAccounts, Is.True);
        });

        await vm.NextCommand.ExecuteAsync(null);
        var savedChannel = catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_MAIN"));
        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(3));
            Assert.That(adapter.VerifyCalls, Is.EqualTo(1));
            Assert.That(vm.IsVerificationSuccess, Is.True);
            Assert.That(vm.CanRetryVerification, Is.False);
            Assert.That(vm.VerificationHeading, Is.EqualTo("Готово к работе"));
            Assert.That(savedChannel?.BotVerifiedSubjectId, Is.EqualTo("UC_BOT"));
        });
    }

    [Test]
    public async Task PlatformSetupWizard_oauth_auto_advances_to_step3_and_shows_retry_only_when_verification_fails()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"));

        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "7700",
                    DisplayName = "streamer_one",
                    IsConfigured = true,
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "7700" }],
        });

        var shouldPass = false;
        var adapter = new StubChannelAdapter(StreamingPlatform.Twitch, () => twitchAccounts.ListAccounts())
        {
            VerifyHandler = (_, _, _) => shouldPass
                ? new(true, "1001")
                : new(false, null, "Нет прав бота на канале"),
        };

        var oauth = new StubTwitchOAuthService(new OAuthFlowResult(
            "access-1",
            "refresh-1",
            [.. TwitchScopes.BotRequired],
            "bot_login",
            "1001",
            3600));
        var launcher = DispatchProxy.Create<IShellLauncher, InertLauncher>();

        var vm = new MultistreamPlatformSetupDialogViewModel(
            catalog, [adapter], twitchAccounts, ytAccounts, ytSettings,
            twitchOAuth: oauth, shellLauncher: launcher);
        vm.Initialize(StreamingPlatform.Twitch);

        await vm.NextCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentStep, Is.EqualTo(2));

        await vm.AuthorizeBotCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(3));
            Assert.That(adapter.VerifyCalls, Is.EqualTo(1));
            Assert.That(vm.IsVerificationSuccess, Is.False);
            Assert.That(vm.CanRetryVerification, Is.True);
            Assert.That(vm.VerificationMessage, Does.Contain("Нет прав бота"));
        });

        shouldPass = true;
        await vm.RetryVerificationCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(adapter.VerifyCalls, Is.EqualTo(2));
            Assert.That(vm.IsVerificationSuccess, Is.True);
            Assert.That(vm.CanRetryVerification, Is.False);
            Assert.That(catalog.FindEntry(new(StreamingPlatform.Twitch, "7700"))?.BotVerifiedSubjectId, Is.EqualTo("1001"));
        });
    }

    [Test]
    public async Task PlatformSetupWizard_adds_a_role_only_to_the_selected_and_persisted_account()
    {
        var accountsPath = Path.Combine(_directory, "role-accounts.json");
        var accounts = new AccountsStore(filePath: accountsPath);
        accounts.UpsertContext(PlatformAuthRole.Bot, AuthContextIds.Default, "operator", new()
        {
            UserId = "1001", Login = "operator", AccessToken = "existing", StoredScopes = [.. TwitchScopes.BotRequired],
        });
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "role-channels.json"));
        var youtube = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "role-youtube-accounts.json"));
        var settings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "role-youtube.json"));
        var launcher = DispatchProxy.Create<IShellLauncher, InertLauncher>();

        foreach (var id in new[] { "1002", "1001" })
        {
            var oauth = new StubTwitchOAuthService(new("new-access", "new-refresh",
                [.. TwitchScopes.BroadcasterRequired], "operator", id, 3600));
            var wizard = new MultistreamPlatformSetupDialogViewModel(catalog, [], accounts, youtube, settings,
                twitchOAuth: oauth, shellLauncher: launcher);
            wizard.Initialize(StreamingPlatform.Twitch);
            await wizard.NextCommand.ExecuteAsync(null);

            var chosen = wizard.ExistingAccounts.Single();
            Assert.That(wizard.AddBroadcasterRoleCommand.CanExecute(chosen), Is.True);
            await wizard.AddBroadcasterRoleCommand.ExecuteAsync(chosen);
            var reopened = new AccountsStore(filePath: accountsPath);
            if (id == "1002")
            {
                Assert.That(wizard.CurrentStep, Is.EqualTo(2));
                Assert.That(wizard.StepNotice, Does.Contain("другой аккаунт"));
                Assert.That(reopened.LoadBroadcaster().AccessToken, Is.Empty);
            }
            else
            {
                Assert.That(wizard.CurrentStep, Is.EqualTo(3));
                Assert.That(wizard.StepNotice, Does.Contain("сохранена и проверена"));
                Assert.That(reopened.LoadBroadcaster().UserId, Is.EqualTo("1001"));
                Assert.That(reopened.LoadBot().AccessToken, Is.EqualTo("existing"));
            }
        }
    }

    [Test]
    public async Task PlatformSetupWizard_does_not_continue_when_authorization_exists_only_in_memory()
    {
        var path = Path.Combine(_directory, "accounts.json");
        var gate = new SettingsWriteGate();
        var accounts = new AccountsStore(filePath: path, gate: gate);
        await gate.RunExternalWriteAsync(() => Task.FromResult(path), file => [file]);
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var youtube = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        var settings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"));
        var oauth = new StubTwitchOAuthService(new("access", "refresh", [.. TwitchScopes.BotRequired], "bot", "1001", 3600));
        var wizard = new MultistreamPlatformSetupDialogViewModel(catalog, [], accounts, youtube, settings,
            twitchOAuth: oauth, shellLauncher: DispatchProxy.Create<IShellLauncher, InertLauncher>());
        wizard.Initialize(StreamingPlatform.Twitch);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.AuthorizeBotCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(wizard.CurrentStep, Is.EqualTo(2));
            Assert.That(wizard.StepNotice, Does.Contain("запись авторизации"));
            Assert.That(wizard.HasSavedChanges, Is.False);
            Assert.That(new AccountsStore(filePath: path).ListAccounts(), Is.Empty);
        });
    }

    [Test]
    public async Task ChannelWizard_add_flow_saves_immediately_and_runs_automatic_verification()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));

        twitchAccounts.SaveAll(
            new TwitchAccountSettings { Login = "bot_acc", UserId = "1001", AccessToken = "tok", RefreshToken = "ref" },
            new TwitchAccountSettings());

        var adapter = new StubChannelAdapter(StreamingPlatform.Twitch, () => twitchAccounts.ListAccounts())
        {
            ResolveHandler = _ => new(
                new(StreamingPlatform.Twitch, "9001"),
                "CoolStreamer",
                "coolstreamer",
                "https://twitch.tv/coolstreamer"),
            VerifyHandler = (_, _, _) => new(true, "1001"),
        };

        var vm = new MultistreamChannelWizardDialogViewModel(catalog, [adapter], twitchAccounts, ytAccounts);
        vm.InitializeForAdd(StreamingPlatform.Twitch);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(1));
            Assert.That(vm.MakeActiveChannel, Is.True);
        });

        vm.ChannelInput = "https://twitch.tv/coolstreamer";
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(2));
            Assert.That(vm.TargetChannelId, Is.EqualTo("9001"));
            Assert.That(vm.TargetChannelDisplayName, Is.EqualTo("CoolStreamer"));
            Assert.That(vm.SelectedBotAccount?.AccountId, Is.EqualTo("1001"));
        });

        await vm.NextCommand.ExecuteAsync(null);

        var stored = catalog.FindEntry(new(StreamingPlatform.Twitch, "9001"));
        var active = catalog.GetActiveChannel(StreamingPlatform.Twitch);
        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(3));
            Assert.That(vm.HasSavedChanges, Is.True);
            Assert.That(vm.HasVerificationFailure, Is.False);
            Assert.That(adapter.VerifyCalls, Is.EqualTo(1));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored?.BotAccountId, Is.EqualTo("1001"));
            Assert.That(stored?.BotVerifiedSubjectId, Is.EqualTo("1001"));
            Assert.That(active?.ChannelId, Is.EqualTo("9001"));
            Assert.That(vm.VerificationResults, Has.Count.EqualTo(1));
            Assert.That(vm.VerificationResults[0].IsPassed, Is.True);
        });
    }

    [Test]
    public async Task ChannelWizard_assign_account_starts_at_step2_and_exposes_retry_only_on_failure()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));

        ytAccounts.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref" },
            new YouTubeAccountSettings { ChannelId = "UC_OWN", ChannelTitle = "OwnerYT", AccessToken = "tok2", RefreshToken = "ref2" });
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_OWN",
                    DisplayName = "@OwnerYT",
                    IsConfigured = true,
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_OWN" }],
        });

        var failBroadcaster = true;
        var adapter = new StubChannelAdapter(StreamingPlatform.YouTube, () => ytAccounts.ListAccounts())
        {
            VerifyHandler = (_, role, _) => role == PlatformAuthRole.Bot
                ? new(true, "UC_BOT")
                : failBroadcaster
                    ? new(false, null, "Нет прав владельца")
                    : new(true, "UC_OWN"),
        };

        var vm = new MultistreamChannelWizardDialogViewModel(catalog, [adapter], twitchAccounts, ytAccounts);
        vm.InitializeForAssignAccount(StreamingPlatform.YouTube, "UC_OWN");

        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(2));
            Assert.That(vm.CanShowBack, Is.False);
            Assert.That(vm.TargetChannelId, Is.EqualTo("UC_OWN"));
            Assert.That(vm.SelectedBotAccount?.AccountId, Is.EqualTo("UC_BOT"));
        });

        vm.SelectedBroadcasterAccount = vm.BroadcasterAccountOptions.Single(o => o.AccountId == "UC_OWN");
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentStep, Is.EqualTo(3));
            Assert.That(vm.HasVerificationFailure, Is.True);
            Assert.That(vm.VerificationResults, Has.Count.EqualTo(2));
            Assert.That(vm.VerificationResults[0].IsPassed, Is.True);
            Assert.That(vm.VerificationResults[1].IsPassed, Is.False);
            Assert.That(catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_OWN"))?.BotVerifiedSubjectId, Is.EqualTo("UC_BOT"));
            Assert.That(catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_OWN"))?.BroadcasterVerifiedSubjectId, Is.Null);
        });

        failBroadcaster = false;
        await vm.RetryVerificationCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasVerificationFailure, Is.False);
            Assert.That(vm.VerificationResults.All(static item => item.IsPassed), Is.True);
            Assert.That(catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_OWN"))?.BroadcasterVerifiedSubjectId, Is.EqualTo("UC_OWN"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Wizard_dialog_views_load_cleanly_and_implement_IView_pairing()
    {
        TestResources.Ensure(ViewDictionaries);

        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"));

        var platformVm = new MultistreamPlatformSetupDialogViewModel(catalog, [], twitchAccounts, ytAccounts, ytSettings);
        platformVm.Initialize(StreamingPlatform.YouTube);
        var platformView = new MultistreamPlatformSetupDialogView { DataContext = platformVm };

        var channelVm = new MultistreamChannelWizardDialogViewModel(catalog, [], twitchAccounts, ytAccounts);
        channelVm.InitializeForAdd(StreamingPlatform.Twitch);
        var channelView = new MultistreamChannelWizardDialogView { DataContext = channelVm };

        Assert.Multiple(() =>
        {
            Assert.That(platformView, Is.InstanceOf<IView<MultistreamPlatformSetupDialogViewModel>>());
            Assert.That(channelView, Is.InstanceOf<IView<MultistreamChannelWizardDialogViewModel>>());
            Assert.That(platformView.DataContext, Is.SameAs(platformVm));
            Assert.That(channelView.DataContext, Is.SameAs(channelVm));
        });
    }

    [Test]
    public async Task ChannelWizard_warns_when_write_gate_blocks_accounts_or_catalog_persistence()
    {
        var twitchPath = Path.Combine(_directory, "gw-accounts.json");
        var youtubePath = Path.Combine(_directory, "gw-youtube-accounts.json");
        var catalogPath = Path.Combine(_directory, "gw-channels.json");
        var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "gw-youtube.json"));
        youtubeSettings.Save(new() { Enabled = true, ClientId = "yt-cid" });

        var gate = new SettingsWriteGate();
        var twitchAccounts = new AccountsStore(filePath: twitchPath, gate: gate);
        var ytAccounts = new YouTubeAccountsStore(filePath: youtubePath, gate: gate);
        ytAccounts.SaveClientSecret("yt-secret");
        var catalog = new ChannelCatalogStore(filePath: catalogPath, gate: gate);

        await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => [twitchPath, youtubePath, catalogPath]);

        var launcher = DispatchProxy.Create<IShellLauncher, InertLauncher>();
        var twitchOAuth = new StubTwitchOAuthService(new("tw-acc", "tw-ref", [.. TwitchScopes.BotRequired], "bot_user", "1001", 3600));
        var twitchAdapter = new StubChannelAdapter(StreamingPlatform.Twitch, () => twitchAccounts.ListAccounts())
        {
            ResolveHandler = _ => new(new(StreamingPlatform.Twitch, "9001"), "Cool", "cool", "https://twitch.tv/cool"),
            VerifyHandler = (_, _, _) => new(true, "1001"),
        };

        var twitchVm = new MultistreamChannelWizardDialogViewModel(
            catalog, [twitchAdapter], twitchAccounts, ytAccounts, youtubeSettings,
            twitchOAuth: twitchOAuth, shellLauncher: launcher);
        twitchVm.InitializeForAdd(StreamingPlatform.Twitch);
        twitchVm.ChannelInput = "cool";
        await twitchVm.NextCommand.ExecuteAsync(null);
        await twitchVm.AuthorizeNewAccountCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(twitchVm.StepNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(twitchVm.StepNotice, Does.Contain("до перезапуска"));
            Assert.That(twitchVm.SelectedBotAccount?.AccountId, Is.EqualTo("1001"));
            Assert.That(new AccountsStore(filePath: twitchPath).ListAccounts(), Is.Empty);
        });

        await twitchVm.NextCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(twitchVm.CurrentStep, Is.EqualTo(3));
            Assert.That(twitchVm.HasVerificationFailure, Is.False);
            Assert.That(twitchVm.VerificationSummarySeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(twitchVm.VerificationSummaryHeading, Is.EqualTo("Применено до перезапуска"));
            Assert.That(new ChannelCatalogStore(filePath: catalogPath).FindEntry(new(StreamingPlatform.Twitch, "9001")), Is.Null);
        });

        var ytOAuth = new StubYouTubeOAuthService(new("yt-acc", "yt-ref", [.. YouTubeScopes.CombinedDefault], "UC_BOT", "BotYT", 3600));
        var ytVm = new MultistreamChannelWizardDialogViewModel(
            catalog, [], twitchAccounts, ytAccounts, youtubeSettings,
            youtubeOAuth: ytOAuth, shellLauncher: launcher);
        ytVm.InitializeForAdd(StreamingPlatform.YouTube);
        await ytVm.AuthorizeNewAccountCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(ytVm.StepNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(ytVm.StepNotice, Does.Contain("до перезапуска"));
            Assert.That(new YouTubeAccountsStore(filePath: youtubePath).ListAccounts(), Is.Empty);
        });
    }

    [Test]
    public async Task ChannelWizard_assign_account_on_auto_discovered_unconfigured_channel_enables_monitoring()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "auto-channels.json"));
        var twitchAccounts = new AccountsStore(filePath: Path.Combine(_directory, "auto-accounts.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "auto-yt-accounts.json"));

        ytAccounts.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "BotYT", AccessToken = "tok", RefreshToken = "ref" },
            new YouTubeAccountSettings());

        catalog.EnsureChannelKnown(new(StreamingPlatform.YouTube, "UC_AUTO"), "AutoChannel");
        var before = catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_AUTO"));
        Assert.That(before, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(before!.IsConfigured, Is.False);
            Assert.That(before.MonitoringEnabled, Is.False);
        });

        var adapter = new StubChannelAdapter(StreamingPlatform.YouTube, () => ytAccounts.ListAccounts())
        {
            ResolveHandler = _ => new(
                new(StreamingPlatform.YouTube, "UC_AUTO"),
                "AutoChannel",
                "@AutoChannel",
                "https://youtube.com/channel/UC_AUTO"),
            VerifyHandler = (_, _, _) => new(true, "UC_BOT"),
        };

        var vm = new MultistreamChannelWizardDialogViewModel(catalog, [adapter], twitchAccounts, ytAccounts);
        vm.InitializeForAdd(StreamingPlatform.YouTube);
        vm.ChannelInput = "UC_AUTO";
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        var after = catalog.FindEntry(new(StreamingPlatform.YouTube, "UC_AUTO"));
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.IsConfigured, Is.True);
            Assert.That(after.MonitoringEnabled, Is.True);
            Assert.That(after.BotAccountId, Is.EqualTo("UC_BOT"));
        });
    }

    private sealed class StubChannelAdapter(
        StreamingPlatform platform,
        Func<IReadOnlyList<PlatformAccount>> listAccounts) : IPlatformChannelAdapter
    {
        public Func<string, ResolvedChannelIdentity?>? ResolveHandler { get; init; }

        public Func<ChannelKey, PlatformAuthRole, string, ChannelAccessVerification>? VerifyHandler { get; init; }

        public int VerifyCalls { get; private set; }

        public StreamingPlatform Platform => platform;

        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;

        public IReadOnlyList<PlatformAccount> ListAccounts() => listAccounts();

        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken) =>
            Task.FromResult(ResolveHandler?.Invoke(rawInput));

        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
        {
            VerifyCalls++;
            return Task.FromResult(VerifyHandler?.Invoke(channel, role, contextId) ?? new ChannelAccessVerification(false, null, "Not configured"));
        }

        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken) =>
            Task.FromResult(new ChannelLiveProbeResult(false, false, null));
    }

    private sealed class StubTwitchOAuthService(OAuthFlowResult flowResult) : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            Task.FromResult(flowResult.AccessToken);

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            Task.FromResult(flowResult);

        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult(flowResult.AccessToken);
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(flowResult.AccessToken);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class StubYouTubeOAuthService(YouTubeOAuthFlowResult flowResult) : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default) =>
            Task.FromResult(flowResult);

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default) =>
            Task.FromResult(flowResult);

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
            Task.FromResult(flowResult.AccessToken);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) =>
            Task.FromResult<string?>(flowResult.AccessToken);

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }

    public class InertLauncher : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
