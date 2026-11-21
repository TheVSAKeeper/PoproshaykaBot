using System.Net;
using System.Text;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Tests.Auth;

[TestFixture]
public sealed class SharedTokenAndAccountRemovalTests
{
    private DirectoryInfo _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("shared-token-auth-");
    }

    [TearDown]
    public void TearDown()
    {
        _dir.Delete(recursive: true);
    }

    [Test]
    public void AccountsStore_RemoveRoleContext_and_RemoveAccount_affect_only_targeted_entries()
    {
        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts.json"));
        store.SaveAll(
            new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a-bot", RefreshToken = "r-bot" },
            new TwitchAccountSettings { UserId = "200", Login = "beta", AccessToken = "b-host", RefreshToken = "r-host" });
        store.UpsertContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-100", "alpha",
            new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a-host", RefreshToken = "r-bot" });
        store.UpsertContext(PlatformAuthRole.Bot, "tw-bot-200", "beta",
            new TwitchAccountSettings { UserId = "200", Login = "beta", AccessToken = "b-bot", RefreshToken = "r-bbot" });

        var (roleChanged, roleWritten) = store.RemoveRoleContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-100");
        Assert.Multiple(() =>
        {
            Assert.That(roleChanged, Is.True);
            Assert.That(roleWritten, Is.True);
            Assert.That(store.TryLoadContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-100", out _), Is.False);
            Assert.That(store.Load(PlatformAuthRole.Bot).UserId, Is.EqualTo("100"));
        });

        var (removed, accountWritten) = store.RemoveAccount("200");
        Assert.Multiple(() =>
        {
            Assert.That(accountWritten, Is.True);
            Assert.That(removed, Has.Count.EqualTo(2));
            Assert.That(store.Load(PlatformAuthRole.Broadcaster).UserId, Is.Empty);
            Assert.That(store.TryLoadContext(PlatformAuthRole.Bot, "tw-bot-200", out _), Is.False);
            Assert.That(store.ListAccounts().Select(a => a.Key.AccountId), Is.EqualTo(new[] { "100" }));
        });
    }

    [Test]
    public void YouTubeAccountsStore_RemoveRoleContext_and_RemoveAccount_affect_only_targeted_entries()
    {
        var store = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yt-accounts.json"));
        store.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_1", ChannelTitle = "One", AccessToken = "a", RefreshToken = "ra" },
            new YouTubeAccountSettings { ChannelId = "UC_2", ChannelTitle = "Two", AccessToken = "b", RefreshToken = "rb" });
        store.UpsertContext(PlatformAuthRole.Bot, "yt-bot-2", "Two",
            new YouTubeAccountSettings { ChannelId = "UC_2", ChannelTitle = "Two", AccessToken = "b2", RefreshToken = "rb" });

        var (removed, written) = store.RemoveAccount("UC_2");
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(removed, Has.Count.EqualTo(2));
            Assert.That(store.Load(PlatformAuthRole.Broadcaster).ChannelId, Is.Empty);
            Assert.That(store.TryLoadContext(PlatformAuthRole.Bot, "yt-bot-2", out _), Is.False);
            Assert.That(store.Load(PlatformAuthRole.Bot).ChannelId, Is.EqualTo("UC_1"));
            Assert.That(store.LoadClientSecret(), Is.EqualTo("secret"));
        });
    }

    [Test]
    public async Task TwitchOAuthService_concurrent_refresh_of_shared_refresh_token_refreshes_once_and_syncs_both_roles()
    {
        var settings = new AppSettings();
        settings.Twitch.ClientId = "cid";
        settings.Twitch.ClientSecret = "csec";
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts-sync.json"));
        var clock = new TestTimeProvider { UtcNow = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero) };
        string[] combinedScopes = [.. TwitchScopes.CombinedDefault];

        var expiredBot = new TwitchAccountSettings
        {
            UserId = "42",
            Login = "streamer42",
            AccessToken = "old-access",
            RefreshToken = "shared-refresh-v1",
            Scopes = [.. combinedScopes],
            StoredScopes = [.. combinedScopes],
            AccessTokenExpiresAt = clock.UtcNow.AddMinutes(-10),
        };
        var expiredBroadcaster = new TwitchAccountSettings
        {
            UserId = "42",
            Login = "streamer42",
            AccessToken = "old-access",
            RefreshToken = "shared-refresh-v1",
            Scopes = [.. combinedScopes],
            StoredScopes = [.. combinedScopes],
            AccessTokenExpiresAt = clock.UtcNow.AddMinutes(-10),
        };
        store.UpsertContext(PlatformAuthRole.Bot, "tw-bot-42", "streamer42", expiredBot);
        store.UpsertContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-42", "streamer42", expiredBroadcaster);

        var tokenCalls = 0;
        var firstEnteredHttp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHttp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHttpHandler(async (request, ct) =>
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("/oauth2/token", StringComparison.Ordinal))
            {
                var call = Interlocked.Increment(ref tokenCalls);
                if (call == 1)
                {
                    firstEnteredHttp.TrySetResult();
                    await releaseFirstHttp.Task.WaitAsync(ct);
                }

                if (call > 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"status":400,"message":"Invalid refresh token"}""", Encoding.UTF8, "application/json"),
                    };
                }

                var scopesJson = string.Join(",", combinedScopes.Select(s => $"\"{s}\""));
                var body = $"{{\"access_token\":\"new-access-v2\",\"refresh_token\":\"shared-refresh-v2\",\"expires_in\":14400,\"scope\":[{scopesJson}]}}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }

            if (uri.Contains("/oauth2/validate", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"login":"streamer42","user_id":"42","expires_in":14400}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var eventBus = Substitute.For<IEventBus>();
        var statusReporter = new OAuthStatusReporter(NullLogger<OAuthStatusReporter>.Instance);
        var tokenClient = new OAuthTokenClient(httpFactory, NullLogger<OAuthTokenClient>.Instance);
        var writer = new OAuthAccountWriter(store, eventBus, statusReporter, clock, NullLogger<OAuthAccountWriter>.Instance);
        using var flow = new OAuthFlowCoordinator(tokenClient, writer, statusReporter, store, settingsManager, NullLogger<OAuthFlowCoordinator>.Instance);
        using var refresher = new OAuthTokenRefresher(tokenClient, writer, statusReporter, store, settingsManager, eventBus, clock, NullLogger<OAuthTokenRefresher>.Instance);
        using var service = new TwitchOAuthService(flow, refresher, tokenClient, writer, statusReporter);

        var botTask = service.GetAccessTokenAsync(PlatformAuthRole.Bot, "tw-bot-42");
        await firstEnteredHttp.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var broadcasterTask = service.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, "tw-broadcaster-42");
        Assert.That(broadcasterTask.IsCompleted, Is.False);
        releaseFirstHttp.TrySetResult();
        var results = await Task.WhenAll(botTask, broadcasterTask);

        var botAfter = store.Load(PlatformAuthRole.Bot, "tw-bot-42");
        var broadcasterAfter = store.Load(PlatformAuthRole.Broadcaster, "tw-broadcaster-42");
        Assert.Multiple(() =>
        {
            Assert.That(tokenCalls, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(new[] { "new-access-v2", "new-access-v2" }));
            Assert.That(botAfter.AccessToken, Is.EqualTo("new-access-v2"));
            Assert.That(botAfter.RefreshToken, Is.EqualTo("shared-refresh-v2"));
            Assert.That(broadcasterAfter.AccessToken, Is.EqualTo("new-access-v2"));
            Assert.That(broadcasterAfter.RefreshToken, Is.EqualTo("shared-refresh-v2"));
        });
    }

    [Test]
    public async Task In_flight_refresh_does_not_resurrect_signed_out_default_or_named_account()
    {
        var settings = new AppSettings();
        settings.Twitch.ClientId = "cid";
        settings.Twitch.ClientSecret = "csec";
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts-signout-race.json"));
        var clock = new TestTimeProvider { UtcNow = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero) };
        string[] scopes = [.. TwitchScopes.BotRequired];

        store.SaveAll(
            new TwitchAccountSettings
            {
                UserId = "55",
                Login = "leaver",
                AccessToken = "old-access",
                RefreshToken = "refresh-v1",
                Scopes = [.. scopes],
                StoredScopes = [.. scopes],
                AccessTokenExpiresAt = clock.UtcNow.AddMinutes(-10),
            },
            new TwitchAccountSettings());

        var enteredHttp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHttp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHttpHandler(async (request, ct) =>
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("/oauth2/token", StringComparison.Ordinal))
            {
                enteredHttp.TrySetResult();
                await releaseHttp.Task.WaitAsync(ct);
                var scopesJson = string.Join(",", scopes.Select(s => $"\"{s}\""));
                var body = $"{{\"access_token\":\"new-access\",\"refresh_token\":\"refresh-v2\",\"expires_in\":14400,\"scope\":[{scopesJson}]}}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }

            if (uri.Contains("/oauth2/validate", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"login":"leaver","user_id":"55","expires_in":14400}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var eventBus = Substitute.For<IEventBus>();
        var statusReporter = new OAuthStatusReporter(NullLogger<OAuthStatusReporter>.Instance);
        var tokenClient = new OAuthTokenClient(httpFactory, NullLogger<OAuthTokenClient>.Instance);
        var writer = new OAuthAccountWriter(store, eventBus, statusReporter, clock, NullLogger<OAuthAccountWriter>.Instance);
        using var flow = new OAuthFlowCoordinator(tokenClient, writer, statusReporter, store, settingsManager, NullLogger<OAuthFlowCoordinator>.Instance);
        using var refresher = new OAuthTokenRefresher(tokenClient, writer, statusReporter, store, settingsManager, eventBus, clock, NullLogger<OAuthTokenRefresher>.Instance);
        using var service = new TwitchOAuthService(flow, refresher, tokenClient, writer, statusReporter);

        var refreshTask = service.GetAccessTokenAsync(PlatformAuthRole.Bot);
        await enteredHttp.Task.WaitAsync(TimeSpan.FromSeconds(5));

        store.RemoveAccount("55");
        releaseHttp.TrySetResult();
        await refreshTask;

        Assert.Multiple(() =>
        {
            Assert.That(store.ListAccounts(), Is.Empty);
            Assert.That(store.LoadBot().AccessToken, Is.Empty);
            Assert.That(store.LoadBot().RefreshToken, Is.Empty);
        });
    }

    [Test]
    public async Task RefreshTokenWithResultAsync_reports_persisted_false_when_write_gate_blocks()
    {
        var settings = new AppSettings();
        settings.Twitch.ClientId = "cid";
        settings.Twitch.ClientSecret = "csec";
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(settings);

        var filePath = Path.Combine(_dir.FullName, "accounts-gated.json");
        var gate = new SettingsWriteGate();
        var store = new AccountsStore(filePath: filePath, gate: gate);
        var clock = new TestTimeProvider { UtcNow = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero) };
        string[] scopes = [.. TwitchScopes.BotRequired];

        store.SaveAll(
            new TwitchAccountSettings
            {
                UserId = "66",
                Login = "gated",
                AccessToken = "old-access",
                RefreshToken = "refresh-v1",
                Scopes = [.. scopes],
                StoredScopes = [.. scopes],
            },
            new TwitchAccountSettings());
        await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => [Path.GetFileName(filePath)]);

        using var handler = new DelegateHttpHandler((request, _) =>
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("/oauth2/token", StringComparison.Ordinal))
            {
                var scopesJson = string.Join(",", scopes.Select(s => $"\"{s}\""));
                var body = $"{{\"access_token\":\"new-access\",\"refresh_token\":\"refresh-v2\",\"expires_in\":14400,\"scope\":[{scopesJson}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"login":"gated","user_id":"66","expires_in":14400}""", Encoding.UTF8, "application/json"),
            });
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var eventBus = Substitute.For<IEventBus>();
        var statusReporter = new OAuthStatusReporter(NullLogger<OAuthStatusReporter>.Instance);
        var tokenClient = new OAuthTokenClient(httpFactory, NullLogger<OAuthTokenClient>.Instance);
        var writer = new OAuthAccountWriter(store, eventBus, statusReporter, clock, NullLogger<OAuthAccountWriter>.Instance);
        using var flow = new OAuthFlowCoordinator(tokenClient, writer, statusReporter, store, settingsManager, NullLogger<OAuthFlowCoordinator>.Instance);
        using var refresher = new OAuthTokenRefresher(tokenClient, writer, statusReporter, store, settingsManager, eventBus, clock, NullLogger<OAuthTokenRefresher>.Instance);
        using var service = new TwitchOAuthService(flow, refresher, tokenClient, writer, statusReporter);

        var result = await service.RefreshTokenWithResultAsync(PlatformAuthRole.Bot, AuthContextIds.Default, "cid", "csec", "refresh-v1");

        Assert.Multiple(() =>
        {
            Assert.That(result.AccessToken, Is.EqualTo("new-access"));
            Assert.That(result.Persisted, Is.False);
        });
    }

    [Test]
    public void RemoveAccountRole_and_UpsertContexts_handle_multiple_contexts_atomically()
    {
        var store = new AccountsStore(filePath: Path.Combine(_dir.FullName, "accounts-multi.json"));
        var written = store.UpsertContexts(
        [
            (PlatformAuthRole.Bot, AuthContextIds.Default, "alpha", new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a1", RefreshToken = "r1" }),
            (PlatformAuthRole.Bot, "tw-bot-100", "alpha", new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a2", RefreshToken = "r2" }),
            (PlatformAuthRole.Broadcaster, "tw-broadcaster-100", "alpha", new TwitchAccountSettings { UserId = "100", Login = "alpha", AccessToken = "a3", RefreshToken = "r3" }),
        ]);

        var (removedContexts, roleWritten) = store.RemoveAccountRole("100", PlatformAuthRole.Bot);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(roleWritten, Is.True);
            Assert.That(removedContexts, Is.EquivalentTo(new[] { AuthContextIds.Default, "tw-bot-100" }));
            Assert.That(store.LoadBot().UserId, Is.Empty);
            Assert.That(store.TryLoadContext(PlatformAuthRole.Bot, "tw-bot-100", out _), Is.False);
            Assert.That(store.TryLoadContext(PlatformAuthRole.Broadcaster, "tw-broadcaster-100", out _), Is.True);
        });
    }

    [Test]
    public async Task YouTubeOAuthService_ValidateAsync_posts_form_token_and_returns_channel_info()
    {
        var options = new YouTubeOAuthOptions();
        string? seenUri = null;
        string? seenBody = null;
        using var handler = new DelegateHttpHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith(options.TokenInfoEndpoint, StringComparison.Ordinal))
            {
                seenUri = request.RequestUri.AbsoluteUri;
                seenBody = await request.Content!.ReadAsStringAsync(ct);
                if (seenBody.Contains("bad-token", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error_description":"Invalid Value"}""", Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"scope":"https://www.googleapis.com/auth/youtube.readonly","expires_in":"3599"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[{"id":"UC_123","snippet":{"title":"My Channel"}}]}""", Encoding.UTF8, "application/json"),
            };
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var eventBus = Substitute.For<IEventBus>();
        var clock = new TestTimeProvider { UtcNow = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero) };
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_dir.FullName, "yt-val.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_dir.FullName, "yt-set.json"));
        var reporter = new YouTubeOAuthStatusReporter(NullLogger<YouTubeOAuthStatusReporter>.Instance);
        var tokenClient = new YouTubeOAuthTokenClient(httpFactory, options, NullLogger<YouTubeOAuthTokenClient>.Instance);
        var identityClient = new YouTubeChannelIdentityClient(httpFactory, options, NullLogger<YouTubeChannelIdentityClient>.Instance);
        var writer = new YouTubeOAuthAccountWriter(ytAccounts, eventBus, reporter, clock, NullLogger<YouTubeOAuthAccountWriter>.Instance);
        using var flow = new YouTubeOAuthFlowCoordinator(tokenClient, identityClient, writer, reporter, ytAccounts, ytSettings, options, NullLogger<YouTubeOAuthFlowCoordinator>.Instance);
        using var refresher = new YouTubeOAuthTokenRefresher(tokenClient, writer, reporter, ytAccounts, ytSettings, options, eventBus, clock, NullLogger<YouTubeOAuthTokenRefresher>.Instance);
        using var service = new YouTubeOAuthService(flow, refresher, writer, reporter, tokenClient, identityClient);

        var valid = await service.ValidateAsync("good-token");
        var invalid = await service.ValidateAsync("bad-token");

        Assert.Multiple(() =>
        {
            Assert.That(seenUri, Does.Not.Contain("good-token"));
            Assert.That(seenBody, Does.Contain("access_token=bad-token"));
            Assert.That(valid, Is.Not.Null);
            Assert.That(valid!.ChannelId, Is.EqualTo("UC_123"));
            Assert.That(valid.ChannelTitle, Is.EqualTo("My Channel"));
            Assert.That(valid.ExpiresInSeconds, Is.EqualTo(3599));
            Assert.That(invalid, Is.Null);
        });
    }

    private sealed class DelegateHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }
}
