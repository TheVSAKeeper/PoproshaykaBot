using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Settings;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class PlatformAccountsSectionTests
{
    private DirectoryInfo _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("platform-accounts-vm-");
    }

    [TearDown]
    public void TearDown()
    {
        _dir.Delete(recursive: true);
    }

    [Test]
    public async Task Combined_twitch_login_writes_both_roles_and_syncs_catalog_options()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels = [new() { Platform = StreamingPlatform.Twitch, ChannelId = "777", DisplayName = "MainChannel", IsConfigured = true }],
        });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });

        var oauth = new FakeTwitchOAuthService();
        var browserProfile = Guid.NewGuid().ToString("N");
        var embedded = new FakeEmbeddedTwitchAuth(req =>
            EmbeddedTwitchAuthResult.Completed(new OAuthFlowResult("acc-tok", "ref-tok", [.. TwitchScopes.CombinedDefault], "streamer777", "777", 14400)
            {
                BrowserProfileName = browserProfile,
            }));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var accountsVm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: accountsStore,
            twitchOAuth: oauth,
            settingsManager: settings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            eventBus: bus,
            embeddedTwitchAuth: embedded);

        var catalogVm = new ChannelCatalogSettingsSectionViewModel(
            catalog,
            twitchAccountsStore: accountsStore);
        accountsVm.AccountsChanged += (_, p) => catalogVm.RefreshAccountOptions(p);
        catalogVm.Load();
        accountsVm.Load();

        Assert.That(accountsVm.HasAccounts, Is.False);

        await accountsVm.AuthorizeCombinedCommand.ExecuteAsync(null);

        var reopened = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts.json"));
        var uiSettings = new MemorySettings();
        var shell = DispatchProxy.Create<IShellLauncher, TrueProxy>();
        var pane = new ChatDisplayPaneViewModel(shell, null, uiSettings);
        pane.SetAccounts(reopened, "Сеанс браузера");
        pane.SelectedAccount = pane.AccountChoices.Single(option => option.Id == "777");
        var restoredPane = new ChatDisplayPaneViewModel(shell, null, uiSettings);
        restoredPane.SetAccounts(reopened, "Сеанс браузера");

        Assert.Multiple(() =>
        {
            Assert.That(embedded.LastRequest, Is.Not.Null);
            Assert.That(embedded.LastRequest!.Scopes, Is.EquivalentTo(TwitchScopes.CombinedDefault));
            Assert.That(accountsVm.AccountCount, Is.EqualTo(1));
            Assert.That(accountsVm.StatusNotice, Does.Contain("Запись проверена"));
            var card = accountsVm.Accounts.Single();
            Assert.That(card.DisplayName, Is.EqualTo("streamer777"));
            Assert.That(card.HasBotRole, Is.True);
            Assert.That(card.HasBroadcasterRole, Is.True);
            Assert.That(card.CanAddBotRole, Is.False);
            Assert.That(card.CanAddBroadcasterRole, Is.False);
            Assert.That(card.Grants, Has.Count.EqualTo(2));
            Assert.That(restoredPane.SelectedAccount?.Label, Does.Contain("streamer777").And.Contain("бот").And.Contain("стример"));
            Assert.That(restoredPane.SelectedAccount?.BrowserProfile?.ProfileName, Is.EqualTo(browserProfile));

            var channel = catalogVm.VisibleChannels.Single();
            var botRole = channel.Roles.Single(r => r.Role == PlatformAuthRole.Bot);
            var broadcasterRole = channel.Roles.Single(r => r.Role == PlatformAuthRole.Broadcaster);
            Assert.That(botRole.Accounts.Single(o => o.AccountId == "777").HasRoleGrant, Is.True);
            Assert.That(broadcasterRole.Accounts.Single(o => o.AccountId == "777").HasRoleGrant, Is.True);
        });
    }

    [Test]
    public async Task Additional_platform_uses_shared_login_permissions_and_browser_account_picker()
    {
        var adapter = new AdditionalPlatform();
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "extra-platform-channels.json"));
        catalog.Save(new()
        {
            Channels = [new()
            {
                Platform = adapter.Platform, ChannelId = "channel", DisplayName = "Канал", IsConfigured = true,
                BotAccountId = "account", BotContextId = "grant", BotVerifiedSubjectId = "account",
            }],
        });
        var viewModel = new PlatformAccountsSectionViewModel(catalog, accountDirectories: [adapter], accountLogins: [adapter])
        {
            SelectedPlatform = adapter.Platform,
        };

        await viewModel.AuthorizeBotCommand.ExecuteAsync(null);

        var card = viewModel.Accounts.Single();
        var pane = new ChatDisplayPaneViewModel(DispatchProxy.Create<IShellLauncher, TrueProxy>(), null);
        pane.SetTarget(adapter.Platform, new Uri("https://example.test/chat"), null, shown: true);
        pane.SetAccounts(adapter, "Просмотр чата");
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SupportsAccounts, Is.True);
            Assert.That(viewModel.SupportsBrowserToggle, Is.True);
            Assert.That(card.Grants.Single().Permissions.Single().Status, Is.EqualTo("Право выдано · канал проверен"));
            Assert.That(pane.AccountChoices[1].Label, Is.EqualTo("Example (бот)"));
            Assert.That(pane.AccountChoices[1].BrowserProfile, Is.EqualTo(adapter.BrowserProfile));
        });
    }

    private sealed class AdditionalPlatform : IPlatformAccountDirectory, IPlatformAccountLogin
    {
        private AuthContextSummary? _grant;
        public StreamingPlatform Platform => StreamingPlatform.VkLive;
        public bool CanAuthorize => true;
        public bool SupportsEmbeddedBrowser => true;
        public PlatformChatBrowserProfile BrowserProfile { get; } = new("fixture-folder", "fixture-profile");

        public Task<PlatformAccountLoginResult> AuthorizeAsync(
            PlatformAccountLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _grant = new("grant", request.Roles.Single(), "Example", "account", "Example", true, false)
            {
                ChatBrowserProfile = BrowserProfile,
            };
            return Task.FromResult(new PlatformAccountLoginResult("Вход выполнен", StatusSeverity.Success, [_grant]));
        }

        public IReadOnlyList<PlatformAccount> ListAccounts() => _grant is { } grant ? [new(new(Platform, "account"), "Example", [grant])] : [];

        public IRoleAccountSettings LoadAuthorization(PlatformAuthRole role, string contextId) =>
            new TwitchAccountSettings { AccessToken = "fixture", Scopes = ["write"], StoredScopes = ["write"] };

        public IReadOnlyList<PlatformAccountOperation> GetPermissions(PlatformAuthRole role) =>
            [new("Отправка в чат", true, "write")];

        public bool VerifyPersistedAuthorization(PlatformAuthRole role, string contextId, string accountId) =>
            _grant is { } grant && grant.Role == role && grant.ContextId == contextId && grant.SubjectId == accountId;
    }

    [Test]
    public async Task Technical_mode_validates_refreshes_saves_manual_edits_and_removes_role_or_account()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "channels2.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "10",
                    DisplayName = "Канал 10",
                    IsConfigured = true,
                    BotAccountId = "100",
                    BotContextId = "default",
                    BotVerifiedSubjectId = "100",
                },
            ],
        });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts2.json"));
        accountsStore.SaveAll(
            new TwitchAccountSettings
            {
                UserId = "100",
                Login = "mybot",
                AccessToken = "tok-1",
                RefreshToken = "ref-1",
                Scopes = [.. TwitchScopes.BotRequired],
                StoredScopes = [.. TwitchScopes.BotRequired],
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            },
            new TwitchAccountSettings
            {
                UserId = "100",
                Login = "mybot",
                AccessToken = "tok-host",
                RefreshToken = "ref-host",
                Scopes = [.. TwitchScopes.BroadcasterRequired],
                StoredScopes = [.. TwitchScopes.BroadcasterRequired],
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            });
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings2.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });

        var oauth = new FakeTwitchOAuthService
        {
            ValidationResult = new TokenValidationInfo("mybot", "100", "cid", TwitchScopes.BotRequired, 3600),
        };

        var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: accountsStore,
            twitchOAuth: oauth,
            settingsManager: settings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            eventBus: new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
        vm.Load();

        Assert.That(vm.IsTechnicalMode, Is.False);
        vm.ToggleTechnicalModeCommand.Execute(null);
        Assert.That(vm.IsTechnicalMode, Is.True);

        var card = vm.Accounts.Single();
        Assert.That(card.AssignedChannelsText, Does.Contain("Канал 10 (Бот)"));
        var botGrant = card.Grants.Single(g => g.Role == PlatformAuthRole.Bot);

        await botGrant.ValidateTokenCommand.ExecuteAsync(null);
        Assert.That(botGrant.ActionStatusSeverity, Is.EqualTo(StatusSeverity.Success));

        await botGrant.RefreshTokenCommand.ExecuteAsync(null);
        Assert.That(oauth.RefreshCalls, Is.EqualTo(new[] { (PlatformAuthRole.Bot, AuthContextIds.Default, "ref-1") }));

        card = vm.Accounts.Single();
        botGrant = card.Grants.Single(g => g.Role == PlatformAuthRole.Bot);
        botGrant.AccessTokenValue = "manual-access";
        Assert.That(botGrant.HasManualEdits, Is.True);
        await botGrant.SaveManualEditsCommand.ExecuteAsync(null);
        Assert.That(accountsStore.LoadBot().AccessToken, Is.EqualTo("manual-access"));

        card = vm.Accounts.Single();
        var hostGrant = card.Grants.Single(g => g.Role == PlatformAuthRole.Broadcaster);
        await hostGrant.RemoveRoleCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Accounts.Single().HasBroadcasterRole, Is.False);
            Assert.That(vm.Accounts.Single().HasBotRole, Is.True);
            Assert.That(vm.Accounts.Single().CanAddBroadcasterRole, Is.True);
        });

        await vm.Accounts.Single().SignOutCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasAccounts, Is.False);
            Assert.That(accountsStore.ListAccounts(), Is.Empty);
        });
    }

    [Test]
    public async Task Combined_youtube_login_writes_both_roles_and_supports_signout()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "yt-channels.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yt.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yt-acc.json"));
        ytAccounts.SaveClientSecret("yt-sec");

        var ytOAuth = new FakeYouTubeOAuthService();
        var vm = new PlatformAccountsSectionViewModel(
            catalog,
            youtubeAccountsStore: ytAccounts,
            youtubeOAuth: ytOAuth,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>());
        vm.SelectedPlatform = StreamingPlatform.YouTube;

        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(ytOAuth.LastRequestedScopes, Is.EquivalentTo(YouTubeScopes.CombinedDefault));
            Assert.That(vm.AccountCount, Is.EqualTo(1));
            Assert.That(vm.Accounts.Single().HasBotRole, Is.True);
            Assert.That(vm.Accounts.Single().HasBroadcasterRole, Is.True);
        });

        await vm.Accounts.Single().SignOutCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasAccounts, Is.False);
            Assert.That(ytAccounts.ListAccounts(), Is.Empty);
        });
    }

    [Test]
    public async Task Manual_save_preserves_rotated_tokens_resets_metadata_on_access_edit_and_detects_conflicts()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-edit.json"));
        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "acc-edit.json"));
        store.SaveAll(
            new TwitchAccountSettings
            {
                UserId = "100",
                Login = "alpha",
                AccessToken = "acc-v1",
                RefreshToken = "ref-v1",
                Scopes = [.. TwitchScopes.BotRequired],
                StoredScopes = [.. TwitchScopes.BotRequired],
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            },
            new TwitchAccountSettings());

        var vm = new PlatformAccountsSectionViewModel(catalog, twitchAccountsStore: store);
        vm.Load();

        var grant = vm.Accounts.Single().Grants.Single();
        grant.ScopesText = string.Join(" ", [.. TwitchScopes.BotRequired, TwitchScopes.UserManageWhispers]);

        // Background rotation happens before manual save of scopes only
        store.Mutate(PlatformAuthRole.Bot, a =>
        {
            a.AccessToken = "acc-v2";
            a.RefreshToken = "ref-v2";
        });

        await grant.SaveManualEditsCommand.ExecuteAsync(null);
        var afterScopes = store.LoadBot();
        Assert.Multiple(() =>
        {
            Assert.That(afterScopes.AccessToken, Is.EqualTo("acc-v2"));
            Assert.That(afterScopes.RefreshToken, Is.EqualTo("ref-v2"));
            Assert.That(afterScopes.Scopes, Does.Contain(TwitchScopes.UserManageWhispers));
        });

        // Manual access token replacement resets stale expiry and stored scopes
        grant = vm.Accounts.Single().Grants.Single();
        grant.AccessTokenValue = "acc-manual";
        await grant.SaveManualEditsCommand.ExecuteAsync(null);
        var afterAccess = store.LoadBot();
        Assert.Multiple(() =>
        {
            Assert.That(afterAccess.AccessToken, Is.EqualTo("acc-manual"));
            Assert.That(afterAccess.AccessTokenExpiresAt, Is.Null);
            Assert.That(afterAccess.StoredScopes, Is.Empty);
        });

        // Concurrent edit conflict detection
        grant = vm.Accounts.Single().Grants.Single();
        grant.RefreshTokenValue = "ref-manual";
        store.Mutate(PlatformAuthRole.Bot, a => a.RefreshToken = "ref-rotated-v3");
        await grant.SaveManualEditsCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(store.LoadBot().RefreshToken, Is.EqualTo("ref-rotated-v3"));
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
        });
    }

    [Test]
    public async Task Card_add_role_rejects_mismatched_account_and_combined_login_checks_role_scopes()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-guard.json"));
        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "acc-guard.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "set-guard.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });
        store.UpsertContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-100", "alpha",
            new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a", RefreshToken = "r" });

        OAuthFlowResult nextFlow = new("b-acc", "b-ref", [.. TwitchScopes.BotRequired], "bravo", "200", 3600);
        var embedded = new FakeEmbeddedTwitchAuth(_ => EmbeddedTwitchAuthResult.Completed(nextFlow));
        var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: store,
            twitchOAuth: new FakeTwitchOAuthService(),
            settingsManager: settings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            embeddedTwitchAuth: embedded);
        vm.Load();

        // AddBotRole on account 100 receives account 200 -> must reject without saving
        await vm.Accounts.Single().AddBotRoleCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(store.ListAccounts().Select(a => a.Key.AccountId), Is.EqualTo(new[] { "100" }));
        });

        // Combined login with only Bot scopes -> saves only Bot role and warns about Broadcaster
        nextFlow = new("c-acc", "c-ref", [.. TwitchScopes.BotRequired], "charlie", "300", 3600);
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        var charlie = vm.Accounts.Single(c => c.AccountId == "300");
        Assert.Multiple(() =>
        {
            Assert.That(charlie.HasBotRole, Is.True);
            Assert.That(charlie.HasBroadcasterRole, Is.False);
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
        });
    }

    [Test]
    public async Task YouTube_validation_refresh_persistence_warning_and_cross_platform_draft_preservation()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-yt2.json"));
        var twStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, "tw2.json"));
        twStore.SaveAll(
            new TwitchAccountSettings { UserId = "10", Login = "twuser", AccessToken = "tw-a", RefreshToken = "tw-r", Scopes = [.. TwitchScopes.BotRequired], StoredScopes = [.. TwitchScopes.BotRequired] },
            new TwitchAccountSettings());
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "set2.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });

        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts2.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta2.json"));
        ytStore.SaveClientSecret("yt-sec");
        ytStore.SaveAll(
            "yt-sec",
            new YouTubeAccountSettings { ChannelId = "UC_1", ChannelTitle = "YTChan", AccessToken = "yt-a", RefreshToken = "yt-r", Scopes = [.. YouTubeScopes.BotRequired], StoredScopes = [.. YouTubeScopes.BotRequired] },
            new YouTubeAccountSettings());

        var refreshGate = new TaskCompletionSource<OAuthRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var twOAuth = new FakeTwitchOAuthService { CustomRefreshResult = refreshGate.Task };
        var ytOAuth = new FakeYouTubeOAuthService
        {
            ValidationResult = new YouTubeTokenValidationInfo("UC_1", "YTChan", [.. YouTubeScopes.BotRequired], 3600),
        };

        var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: twStore,
            youtubeAccountsStore: ytStore,
            twitchOAuth: twOAuth,
            youtubeOAuth: ytOAuth,
            settingsManager: settings,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>());
        vm.Load();

        var twGrant = vm.Accounts.Single().Grants.Single();
        var twRefreshTask = twGrant.RefreshTokenCommand.ExecuteAsync(null);

        // Switch to YouTube and edit token while Twitch refresh is in flight
        vm.SelectedPlatform = StreamingPlatform.YouTube;
        var ytGrant = vm.Accounts.Single().Grants.Single();
        ytGrant.AccessTokenValue = "yt-edited-unsaved";

        // Twitch refresh finishes without disk persistence
        refreshGate.SetResult(new("tw-new", Persisted: false));
        await twRefreshTask;

        Assert.Multiple(() =>
        {
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(vm.Accounts.Single().Grants.Single().AccessTokenValue, Is.EqualTo("yt-edited-unsaved"));
        });

        await ytGrant.ValidateTokenCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(ytGrant.SupportsTokenValidation, Is.True);
            Assert.That(ytGrant.ActionStatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(ytGrant.ActionStatusText, Does.Contain("YTChan"));
        });
    }

    [TestCase(StreamingPlatform.Twitch)]
    [TestCase(StreamingPlatform.YouTube)]
    public async Task Account_card_displays_api_and_web_chat_statuses_and_toggles_web_session_uniformly(StreamingPlatform platform)
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, $"ch-web-{platform}.json"));
        var twPath = Path.Combine(_dir.FullName, $"tw-web-{platform}.json");
        var ytPath = Path.Combine(_dir.FullName, $"yt-web-{platform}.json");
        var twStore = new AccountsStore(filePath: twPath);
        twStore.SaveAll(
            new TwitchAccountSettings { UserId = "tw-1", Login = "twuser", AccessToken = "tw-a", RefreshToken = "tw-r", Scopes = [.. TwitchScopes.BotRequired], StoredScopes = [.. TwitchScopes.BotRequired] },
            new TwitchAccountSettings());
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, $"set-web-{platform}.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, $"yts-web-{platform}.json"));
        var ytStore = new YouTubeAccountsStore(filePath: ytPath);
        ytStore.SaveAll(
            "yt-sec",
            new YouTubeAccountSettings { ChannelId = "yt-1", ChannelTitle = "YTUser", AccessToken = "yt-a", RefreshToken = "yt-r", Scopes = [.. YouTubeScopes.BotRequired], StoredScopes = [.. YouTubeScopes.BotRequired] },
            new YouTubeAccountSettings());

        var profileId = Guid.NewGuid().ToString("N");
        var webChatLogin = new FakeWebChatLoginDialog(PlatformWebChatLoginResult.Completed(profileId));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var refreshedCount = 0;
        using var sub = bus.Subscribe<PlatformAuthorizationRefreshed>(_ => refreshedCount++);

        var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: twStore,
            youtubeAccountsStore: ytStore,
            twitchOAuth: new FakeTwitchOAuthService(),
            youtubeOAuth: new FakeYouTubeOAuthService(),
            settingsManager: settings,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            eventBus: bus,
            webChatLoginDialog: webChatLogin)
        {
            SelectedPlatform = platform,
        };
        vm.Load();

        var card = vm.Accounts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(card.AuthMethods, Has.Count.EqualTo(2));
            Assert.That(card.ApiAuthMethod.Title, Is.EqualTo("API / OAuth"));
            Assert.That(card.WebChatMethod?.Title, Is.EqualTo("Вход для чата в окне"));
            Assert.That(card.WebChatMethod?.Status, Is.EqualTo(PlatformAuthMethodStatus.Attention));
            Assert.That(card.WebChatStatusText, Is.EqualTo("Вход для чата в окне: не подключён"));
            Assert.That(card.WebChatStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(card.CanSignInWebChat, Is.True);
            Assert.That(card.CanResetWebChat, Is.False);
            Assert.That(card.ReconnectWebChatCommand.CanExecute(null), Is.False);
        });

        await card.SignInWebChatCommand.ExecuteAsync(null);
        var afterSignIn = vm.Accounts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(webChatLogin.LastRequest?.Descriptor.Platform, Is.EqualTo(platform));
            Assert.That(afterSignIn.WebChatStatusText, Is.EqualTo("Вход для чата в окне: подключён"));
            Assert.That(afterSignIn.WebChatStatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(afterSignIn.CanSignInWebChat, Is.False);
            Assert.That(afterSignIn.CanResetWebChat, Is.True);
            Assert.That(afterSignIn.ReconnectWebChatCommand.CanExecute(null), Is.True);
            Assert.That(refreshedCount, Is.GreaterThanOrEqualTo(1));
        });

        await afterSignIn.ReconnectWebChatCommand.ExecuteAsync(null);
        Assert.That(webChatLogin.CallCount, Is.EqualTo(2));

        var countBeforeReset = refreshedCount;
        await vm.Accounts.Single().ResetWebChatCommand.ExecuteAsync(null);
        var afterReset = vm.Accounts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(afterReset.WebChatStatusText, Is.EqualTo("Вход для чата в окне: не подключён"));
            Assert.That(afterReset.WebChatStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(afterReset.CanSignInWebChat, Is.True);
            Assert.That(afterReset.CanResetWebChat, Is.False);
            Assert.That(refreshedCount, Is.GreaterThan(countBeforeReset));
        });
    }

    [Test]
    public async Task CancelAuthorizationCommand_cancels_in_flight_oauth_and_platform_switch_or_dispose_cancels_cleanly()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-cancel.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts-cancel.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta-cancel.json"));
        ytStore.SaveClientSecret("yt-sec");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledTokens = new List<CancellationToken>();
        var ytOAuth = new FakeYouTubeOAuthService
        {
            CustomDraftFlow = async ct =>
            {
                canceledTokens.Add(ct);
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            },
        };

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            youtubeAccountsStore: ytStore,
            youtubeOAuth: ytOAuth,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>());
        vm.SelectedPlatform = StreamingPlatform.YouTube;

        Assert.That(vm.CancelAuthorizationCommand.CanExecute(null), Is.False);
        var authTask = vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        await started.Task;

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAuthorizing, Is.True);
            Assert.That(vm.StatusNotice, Is.EqualTo("Ожидаем вход на площадке…"));
            Assert.That(vm.CancelAuthorizationCommand.CanExecute(null), Is.True);
        });

        vm.CancelAuthorizationCommand.Execute(null);
        await authTask;

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAuthorizing, Is.False);
            Assert.That(vm.StatusNotice, Is.EqualTo("Вход отменён."));
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Info));
            Assert.That(canceledTokens[0].IsCancellationRequested, Is.True);
            Assert.That(ytStore.ListAccounts(), Is.Empty);
        });

        // Platform switch cancels active authorization without polluting new platform status
        started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switchTask = vm.AuthorizeBotCommand.ExecuteAsync(null);
        await started.Task;
        vm.SelectedPlatform = StreamingPlatform.Twitch;
        await switchTask;

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAuthorizing, Is.False);
            Assert.That(vm.StatusNotice, Is.Null);
            Assert.That(canceledTokens[1].IsCancellationRequested, Is.True);
        });

        // Dispose cancels active authorization without throwing
        vm.SelectedPlatform = StreamingPlatform.YouTube;
        started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeTask = vm.AuthorizeBotCommand.ExecuteAsync(null);
        await started.Task;
        vm.Dispose();
        await disposeTask;

        Assert.That(canceledTokens[2].IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task Post_login_web_chat_launches_for_system_browser_flows_skips_when_already_bound_and_preserves_oauth_on_cancel()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-auto-wc.json"));
        var twStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, "tw-auto-wc.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "set-auto-wc.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts-auto-wc.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta-auto-wc.json"));
        ytStore.SaveClientSecret("yt-sec");

        var embeddedProfile = Guid.NewGuid().ToString("N");
        var webProfile = Guid.NewGuid().ToString("N");
        var embedded = new FakeEmbeddedTwitchAuth(_ =>
            EmbeddedTwitchAuthResult.Completed(new OAuthFlowResult("emb-a", "emb-r", [.. TwitchScopes.CombinedDefault], "tw_emb", "101", 3600)
            {
                BrowserProfileName = embeddedProfile,
            }));
        var twOAuth = new FakeTwitchOAuthService
        {
            CustomDraftFlow = _ => Task.FromResult(new OAuthFlowResult("sys-a", "sys-r", [.. TwitchScopes.CombinedDefault], "tw_sys", "202", 3600)),
        };
        var ytOAuth = new FakeYouTubeOAuthService();
        var webChatLogin = new FakeWebChatLoginDialog(_ => PlatformWebChatLoginResult.Completed(webProfile));

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: twStore,
            youtubeAccountsStore: ytStore,
            twitchOAuth: twOAuth,
            youtubeOAuth: ytOAuth,
            settingsManager: settings,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            embeddedTwitchAuth: embedded,
            webChatLoginDialog: webChatLogin);
        vm.Load();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ConnectWebChatAfterLogin, Is.True);
            Assert.That(vm.SupportsWebChatAfterLogin, Is.True);
            Assert.That(vm.SupportsBrowserToggle, Is.True);
            Assert.That(vm.HasOAuthMethodHint, Is.False);
        });

        // 1. Twitch embedded browser binds web chat profile in one step -> must NOT reopen web chat dialog
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(webChatLogin.CallCount, Is.Zero);
            Assert.That(vm.Accounts.Single(a => a.AccountId == "101").HasWebChatSession, Is.True);
        });

        // 2. Twitch with UseSystemBrowser = true -> auto-launches web chat dialog
        vm.UseSystemBrowser = true;
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(webChatLogin.CallCount, Is.EqualTo(1));
            Assert.That(webChatLogin.LastRequest?.Descriptor.Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(webChatLogin.LastRequest?.AccountId, Is.EqualTo("202"));
            Assert.That(vm.Accounts.Single(a => a.AccountId == "202").HasWebChatSession, Is.True);
        });

        // 3. YouTube explains why system browser is used and auto-launches web chat
        vm.SelectedPlatform = StreamingPlatform.YouTube;
        Assert.Multiple(() =>
        {
            Assert.That(vm.SupportsBrowserToggle, Is.False);
            Assert.That(vm.SupportsWebChatAfterLogin, Is.True);
            Assert.That(vm.HasOAuthMethodHint, Is.True);
            Assert.That(vm.OAuthMethodHint, Does.Contain("Google"));
        });

        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(webChatLogin.CallCount, Is.EqualTo(2));
            Assert.That(webChatLogin.LastRequest?.Descriptor.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(vm.Accounts.Single(a => a.AccountId == "UC_99").HasWebChatSession, Is.True);
        });

        // 4. Canceling web chat dialog preserves saved OAuth account
        ytOAuth.NextFlowResult = new YouTubeOAuthFlowResult("yt-acc-2", "yt-ref-2", [.. YouTubeScopes.CombinedDefault], "UC_100", "MyYT2", 3600);
        webChatLogin.Handler = _ => PlatformWebChatLoginResult.Canceled();
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);

        var secondYt = vm.Accounts.Single(a => a.AccountId == "UC_100");
        Assert.Multiple(() =>
        {
            Assert.That(webChatLogin.CallCount, Is.EqualTo(3));
            Assert.That(secondYt.HasWebChatSession, Is.False);
            Assert.That(secondYt.WebChatStatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(secondYt.CanSignInWebChat, Is.True);
            Assert.That(ytStore.ListAccounts().Any(a => a.Key.AccountId == "UC_100"), Is.True);
            Assert.That(vm.StatusNotice, Does.Contain("сохранён"));
        });

        // 5. Turning off ConnectWebChatAfterLogin skips automatic web chat launch
        vm.ConnectWebChatAfterLogin = false;
        ytOAuth.NextFlowResult = new YouTubeOAuthFlowResult("yt-acc-3", "yt-ref-3", [.. YouTubeScopes.CombinedDefault], "UC_101", "MyYT3", 3600);
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.That(webChatLogin.CallCount, Is.EqualTo(3));
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    [Apartment(ApartmentState.STA)]
    public async Task Account_cards_render_web_chat_states_options_and_cancel_button_without_clipping(string theme)
    {
        TestResources.Ensure(
        [
            "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
            "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        ]);

        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, $"ch-vis-{theme}.json"));
        var twStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, $"tw-vis-{theme}.json"));
        twStore.UpsertContext(PlatformAuthRole.Bot, "default", "connected_bot",
            new TwitchAccountSettings
            {
                UserId = "10",
                Login = "connected_bot",
                AccessToken = "a1",
                RefreshToken = "r1",
                Scopes = [.. TwitchScopes.BotRequired],
                StoredScopes = [.. TwitchScopes.BotRequired],
            });
        twStore.SetChatBrowserProfile("10", Guid.NewGuid().ToString("N"));
        twStore.UpsertContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-20", "needs_webchat",
            new TwitchAccountSettings
            {
                UserId = "20",
                Login = "needs_webchat",
                AccessToken = "a2",
                RefreshToken = "r2",
                Scopes = [.. TwitchScopes.BroadcasterRequired],
                StoredScopes = [.. TwitchScopes.BroadcasterRequired],
            });

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, $"set-vis-{theme}.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "csec";
        });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twOAuth = new FakeTwitchOAuthService
        {
            CustomDraftFlow = async ct =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            },
        };
        var embedded = new FakeEmbeddedTwitchAuth(_ => EmbeddedTwitchAuthResult.Canceled("cancel"));
        var webChatLogin = new FakeWebChatLoginDialog(PlatformWebChatLoginResult.Completed(Guid.NewGuid().ToString("N")));

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: twStore,
            twitchOAuth: twOAuth,
            settingsManager: settings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            embeddedTwitchAuth: embedded,
            webChatLoginDialog: webChatLogin)
        {
            UseSystemBrowser = true,
        };
        vm.Load();

        var authTask = vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        await started.Task;

        var view = new PlatformAccountsSectionView { DataContext = vm };
        var host = new ContentControl
        {
            Content = view,
            Padding = new Thickness(16),
        };
        if (Application.Current?.TryFindResource("Font.Sans") is FontFamily fontFamily)
        {
            host.FontFamily = fontFamily;
        }

        if (Application.Current?.TryFindResource("Font.Size.M") is double fontSize)
        {
            host.FontSize = fontSize;
        }

        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new($"pack://application:,,,/KeepShell;component/Resources/Themes/{theme}.xaml"),
        });
        host.SetResourceReference(Control.BackgroundProperty, "Bg.Base");
        host.Measure(new Size(820, 900));
        host.Arrange(new Rect(0, 0, 820, 900));
        host.UpdateLayout();

        var buttons = FindDescendants<Button>(view).Where(IsEffectivelyVisible).ToArray();
        var checkboxes = FindDescendants<CheckBox>(view).Where(IsEffectivelyVisible).ToArray();
        var texts = FindDescendants<TextBlock>(view).Where(IsEffectivelyVisible).Select(t => t.Text).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(buttons.Any(b => Equals(b.Content, "Отменить вход") && b.ActualHeight > 0), Is.True);
            Assert.That(buttons.Any(b => Equals(b.Content, "Вход для чата в окне") && b.ActualHeight > 0), Is.True);
            Assert.That(buttons.Any(b => Equals(b.Content, "Переподключить") && b.ActualHeight > 0), Is.True);
            Assert.That(buttons.Any(b => Equals(b.Content, "Отключить") && b.ActualHeight > 0), Is.True);
            Assert.That(checkboxes.Any(c => Equals(c.Content, "Сразу выполнить вход для чата в окне после авторизации")), Is.True);
            Assert.That(texts, Has.Some.EqualTo("Вход для чата в окне: не подключён"));
            Assert.That(texts, Has.Some.EqualTo("Вход для чата в окне: подключён"));
        });

        TestResources.CaptureAccountLayout(host, $"accounts-ux-{theme}", 820, 900);

        vm.CancelAuthorizationCommand.Execute(null);
        await authTask;
    }

    private static bool IsEffectivelyVisible(UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class FakeWebChatLoginDialog(Func<PlatformWebChatLoginRequest, Task<PlatformWebChatLoginResult>> asyncHandler) : IPlatformWebChatLoginDialog
    {
        public FakeWebChatLoginDialog(Func<PlatformWebChatLoginRequest, PlatformWebChatLoginResult> handler)
            : this(req => Task.FromResult(handler(req)))
        {
        }

        public FakeWebChatLoginDialog(PlatformWebChatLoginResult result)
            : this(_ => Task.FromResult(result))
        {
        }

        public Func<PlatformWebChatLoginRequest, Task<PlatformWebChatLoginResult>> AsyncHandler { get; set; } = asyncHandler;

        public Func<PlatformWebChatLoginRequest, PlatformWebChatLoginResult> Handler
        {
            set => AsyncHandler = req => Task.FromResult(value(req));
        }

        public PlatformWebChatLoginRequest? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        public Task<PlatformWebChatLoginResult> SignInAsync(PlatformWebChatLoginRequest request)
        {
            LastRequest = request;
            CallCount++;
            return AsyncHandler(request);
        }
    }

    private sealed class FakeEmbeddedTwitchAuth(Func<EmbeddedTwitchAuthRequest, CancellationToken, Task<EmbeddedTwitchAuthResult>> asyncHandler) : IEmbeddedTwitchAuthDialog
    {
        public FakeEmbeddedTwitchAuth(Func<EmbeddedTwitchAuthRequest, EmbeddedTwitchAuthResult> handler)
            : this((req, _) => Task.FromResult(handler(req)))
        {
        }

        public EmbeddedTwitchAuthRequest? LastRequest { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return asyncHandler(request, cancellationToken);
        }
    }

    [Test]
    public async Task CancelAuthorizationCommand_cancels_embedded_twitch_auth()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-emb-cancel.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_dir.FullName, "acc-emb-cancel.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "set-emb-cancel.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "cid";
            s.Twitch.ClientSecret = "sec";
        });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var embedded = new FakeEmbeddedTwitchAuth(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        });

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            twitchAccountsStore: accountsStore,
            settingsManager: settings,
            twitchOAuth: new FakeTwitchOAuthService(),
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            embeddedTwitchAuth: embedded);
        vm.SelectedPlatform = StreamingPlatform.Twitch;
        vm.UseSystemBrowser = false;

        var authTask = vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        await started.Task;

        vm.CancelAuthorizationCommand.Execute(null);
        await authTask;

        Assert.Multiple(() =>
        {
            Assert.That(embedded.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(vm.IsAuthorizing, Is.False);
            Assert.That(vm.StatusNotice, Is.EqualTo("Вход отменён."));
        });
    }

    [Test]
    public async Task PostLoginWebChat_when_platform_switches_mid_dialog_does_not_overwrite_new_platform_notice()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-webchat-switch.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts-webchat-switch.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta-webchat-switch.json"));
        ytStore.SaveClientSecret("yt-sec");

        var webChatStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWebChat = new TaskCompletionSource<PlatformWebChatLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var webChat = new FakeWebChatLoginDialog(async _ =>
        {
            webChatStarted.TrySetResult();
            return await releaseWebChat.Task;
        });

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            youtubeAccountsStore: ytStore,
            youtubeOAuth: new FakeYouTubeOAuthService(),
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            webChatLoginDialog: webChat);
        vm.SelectedPlatform = StreamingPlatform.YouTube;
        vm.ConnectWebChatAfterLogin = true;

        var authTask = vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        await webChatStarted.Task;

        vm.SelectedPlatform = StreamingPlatform.Twitch;
        Assert.That(vm.StatusNotice, Is.Null.Or.Empty);

        releaseWebChat.TrySetResult(PlatformWebChatLoginResult.Completed("yt-profile-1"));
        await authTask;

        Assert.That(vm.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(vm.StatusNotice, Is.Null.Or.Empty);
    }

    [Test]
    public async Task PostLoginWebChat_is_skipped_when_oauth_persistence_is_unconfirmed()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-unpersisted.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts-unpersisted.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta-unpersisted.json"));
        ytStore.SaveClientSecret("yt-sec");
        ytStore.UpsertContexts([(PlatformAuthRole.Bot, AuthContextIds.Default, "MyYT", new YouTubeAccountSettings
        {
            AccessToken = "old",
            RefreshToken = "old-r",
            ChannelId = "UC_99",
            ChannelTitle = "MyYT",
        })]);

        var webChat = new FakeWebChatLoginDialog(PlatformWebChatLoginResult.Completed("yt-profile-1"));
        var stubLogin = new UnpersistedAccountLogin(StreamingPlatform.YouTube, "UC_99", "MyYT", webChat);

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            youtubeAccountsStore: ytStore,
            youtubeSettingsStore: ytSettings,
            accountLogins: [stubLogin]);
        vm.SelectedPlatform = StreamingPlatform.YouTube;
        vm.ConnectWebChatAfterLogin = true;

        await vm.AuthorizeBotCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(webChat.CallCount, Is.EqualTo(0));
            Assert.That(vm.StatusNotice, Is.EqualTo("Не удалось подтвердить запись авторизации на диске."));
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Warning));
        });
    }

    [Test]
    public async Task YouTube_embedded_login_binds_web_chat_in_one_step_and_falls_back_to_system_browser_when_toggled()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_dir.FullName, "ch-yt-emb.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yts-yt-emb.json"));
        ytSettings.Save(new() { ClientId = "yt-cid" });
        var ytStore = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yta-yt-emb.json"));
        ytStore.SaveClientSecret("yt-sec");

        var embeddedProfile = Guid.NewGuid().ToString("N");
        var webProfile = Guid.NewGuid().ToString("N");
        var embeddedYt = new FakeEmbeddedYouTubeAuthDialog(
            new YouTubeOAuthFlowResult("yt-emb-at", "yt-emb-rt", [.. YouTubeScopes.CombinedDefault], "UC_EMB_1", "YT Embedded", 3600)
                { BrowserProfileName = embeddedProfile });
        var ytOAuth = new FakeYouTubeOAuthService
        {
            NextFlowResult = new YouTubeOAuthFlowResult("yt-sys-at", "yt-sys-rt", [.. YouTubeScopes.CombinedDefault], "UC_SYS_2", "YT System", 3600),
        };
        var webChat = new FakeWebChatLoginDialog(_ => PlatformWebChatLoginResult.Completed(webProfile));

        using var vm = new PlatformAccountsSectionViewModel(
            catalog,
            youtubeAccountsStore: ytStore,
            youtubeOAuth: ytOAuth,
            youtubeSettingsStore: ytSettings,
            shellLauncher: DispatchProxy.Create<IShellLauncher, TrueProxy>(),
            dialogService: DispatchProxy.Create<IDialogService, TrueProxy>(),
            webChatLoginDialog: webChat,
            embeddedYouTubeAuth: embeddedYt);
        vm.SelectedPlatform = StreamingPlatform.YouTube;

        Assert.Multiple(() =>
        {
            Assert.That(vm.SupportsBrowserToggle, Is.True);
            Assert.That(vm.HasOAuthMethodHint, Is.False);
            Assert.That(vm.BrowserToggleHint, Does.Contain("Google"));
            Assert.That(vm.BrowserToggleHint, Does.Contain("системный браузер"));
        });

        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(embeddedYt.CallCount, Is.EqualTo(1));
            Assert.That(webChat.CallCount, Is.Zero);
            Assert.That(vm.Accounts.Single(a => a.AccountId == "UC_EMB_1").HasWebChatSession, Is.True);
            Assert.That(ytStore.LoadBot().BrowserProfileName, Is.EqualTo(embeddedProfile));
        });

        vm.UseSystemBrowser = true;
        await vm.AuthorizeCombinedCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(embeddedYt.CallCount, Is.EqualTo(1));
            Assert.That(webChat.CallCount, Is.EqualTo(1));
            Assert.That(webChat.LastRequest?.AccountId, Is.EqualTo("UC_SYS_2"));
            Assert.That(vm.Accounts.Single(a => a.AccountId == "UC_SYS_2").HasWebChatSession, Is.True);
        });
    }

    private sealed class FakeEmbeddedYouTubeAuthDialog(YouTubeOAuthFlowResult flow) : IEmbeddedYouTubeAuthDialog
    {
        public int CallCount { get; private set; }

        public Task<EmbeddedYouTubeAuthResult> AuthorizeAsync(
            EmbeddedYouTubeAuthRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(EmbeddedYouTubeAuthResult.Completed(flow));
        }
    }

    private sealed class UnpersistedAccountLogin(
        StreamingPlatform platform,
        string accountId,
        string label,
        IPlatformWebChatLoginDialog webChatDialog) : IPlatformAccountLogin
    {
        public StreamingPlatform Platform => platform;
        public bool CanAuthorize => true;
        public bool SupportsEmbeddedBrowser => false;
        public bool SupportsTokenValidation => false;
        public bool SupportsEditableScopes => false;
        public PlatformWebChatLoginDescriptor? WebChatDescriptor => PlatformWebChatDescriptors.YouTube;
        public PlatformOAuthMethodDescriptor OAuthMethod => new("Системный браузер", "Вход в браузере", false, true);
        public IReadOnlyList<string> GetDefaultScopes(PlatformAuthRole role) => [];
        public string[] MergeRequestedScopes(PlatformAuthRole role, IReadOnlyCollection<string> configuredScopes) => [.. configuredScopes];
        public Task<PlatformAccountLoginResult> AuthorizeAsync(PlatformAccountLoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformAccountLoginResult(
                "Запись не подтверждена.",
                StatusSeverity.Warning,
                [new AuthContextSummary("yt-bot-unwritten", PlatformAuthRole.Bot, label, accountId, label, true, false)]));
        public Task<PlatformTokenValidationResult> ValidateTokenAsync(PlatformAuthRole role, string accessToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PlatformTokenRefreshResult> RefreshTokenAsync(PlatformAuthRole role, string contextId, string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public PlatformManualEditResult SaveManualEdits(PlatformAuthRole role, string contextId, PlatformManualTokenEdits edits) =>
            throw new NotSupportedException();
        public async Task<PlatformWebChatSignInResult> SignInWebChatAsync(string id, string name)
        {
            var r = await webChatDialog.SignInAsync(new(PlatformWebChatDescriptors.YouTube, id, name));
            return new(r.Outcome, r.BrowserProfileName, true, r.Message, StatusSeverity.Success);
        }
        public void ResetRetryState(PlatformAuthRole role, string contextId) { }
    }

    public class TrueProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.ReturnType == typeof(bool))
            {
                return true;
            }

            return targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    private sealed class FakeTwitchOAuthService : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public TokenValidationInfo? ValidationResult { get; set; }
        public Task<OAuthRefreshResult>? CustomRefreshResult { get; set; }
        public Func<CancellationToken, Task<OAuthFlowResult>>? CustomDraftFlow { get; set; }
        public List<(PlatformAuthRole Role, string ContextId, string RefreshToken)> RefreshCalls { get; } = [];

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            CustomDraftFlow is not null ? CustomDraftFlow(ct) : throw new NotSupportedException();

        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(ValidationResult is not null);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult(ValidationResult);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
            RefreshTokenAsync(role, AuthContextIds.Default, clientId, clientSecret, refreshToken, ct);

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string? contextId, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
        {
            RefreshCalls.Add((role, AuthContextIds.Normalize(contextId), refreshToken));
            return Task.FromResult("tok-refreshed");
        }

        public Task<OAuthRefreshResult> RefreshTokenWithResultAsync(PlatformAuthRole role, string? contextId, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
        {
            RefreshCalls.Add((role, AuthContextIds.Normalize(contextId), refreshToken));
            return CustomRefreshResult ?? Task.FromResult(new OAuthRefreshResult("tok-refreshed", Persisted: true));
        }

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class FakeYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public string[]? LastRequestedScopes { get; private set; }
        public YouTubeTokenValidationInfo? ValidationResult { get; set; }
        public Func<CancellationToken, Task<YouTubeOAuthFlowResult>>? CustomDraftFlow { get; set; }
        public YouTubeOAuthFlowResult? NextFlowResult { get; set; }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
        {
            LastRequestedScopes = scopes;
            if (CustomDraftFlow is not null)
            {
                return CustomDraftFlow(ct);
            }

            return Task.FromResult(NextFlowResult ?? new YouTubeOAuthFlowResult("yt-acc", "yt-ref", [.. YouTubeScopes.CombinedDefault], "UC_99", "MyYT", 3600));
        }

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
            Task.FromResult("yt-refreshed");

        public Task<YouTubeTokenValidationInfo?> ValidateAsync(string accessToken, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => true;
        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }
}
