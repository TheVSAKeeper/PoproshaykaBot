using System.IO;
using System.Reflection;
using KeepShell.Services;
using KeepShell.Services.Platform;
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
using PoproshaykaBot.Wpf.ViewModels.Settings;

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
        var embedded = new FakeEmbeddedTwitchAuth(req =>
            EmbeddedTwitchAuthResult.Completed(new OAuthFlowResult("acc-tok", "ref-tok", [.. TwitchScopes.CombinedDefault], "streamer777", "777", 14400)));
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

        Assert.Multiple(() =>
        {
            Assert.That(embedded.LastRequest, Is.Not.Null);
            Assert.That(embedded.LastRequest!.Scopes, Is.EquivalentTo(TwitchScopes.CombinedDefault));
            Assert.That(accountsVm.AccountCount, Is.EqualTo(1));
            var card = accountsVm.Accounts.Single();
            Assert.That(card.DisplayName, Is.EqualTo("streamer777"));
            Assert.That(card.HasBotRole, Is.True);
            Assert.That(card.HasBroadcasterRole, Is.True);
            Assert.That(card.CanAddBotRole, Is.False);
            Assert.That(card.CanAddBroadcasterRole, Is.False);
            Assert.That(card.Grants, Has.Count.EqualTo(2));

            var channel = catalogVm.VisibleChannels.Single();
            var botRole = channel.Roles.Single(r => r.Role == PlatformAuthRole.Bot);
            var broadcasterRole = channel.Roles.Single(r => r.Role == PlatformAuthRole.Broadcaster);
            Assert.That(botRole.Accounts.Single(o => o.AccountId == "777").HasRoleGrant, Is.True);
            Assert.That(broadcasterRole.Accounts.Single(o => o.AccountId == "777").HasRoleGrant, Is.True);
        });
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

    private sealed class FakeEmbeddedTwitchAuth(Func<EmbeddedTwitchAuthRequest, EmbeddedTwitchAuthResult> handler) : IEmbeddedTwitchAuthDialog
    {
        public EmbeddedTwitchAuthRequest? LastRequest { get; private set; }

        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request)
        {
            LastRequest = request;
            return Task.FromResult(handler(request));
        }
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
        public List<(PlatformAuthRole Role, string ContextId, string RefreshToken)> RefreshCalls { get; } = [];

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default) =>
            throw new NotSupportedException();

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

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class FakeYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public string[]? LastRequestedScopes { get; private set; }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
        {
            LastRequestedScopes = scopes;
            return Task.FromResult(new YouTubeOAuthFlowResult("yt-acc", "yt-ref", [.. YouTubeScopes.CombinedDefault], "UC_99", "MyYT", 3600));
        }

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
            Task.FromResult("yt-refreshed");

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => true;
        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }
}
