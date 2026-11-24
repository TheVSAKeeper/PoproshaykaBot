using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.Tests.Auth;

[TestFixture]
public sealed class YouTubeOAuthServiceTests
{
    private string _tempDir = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private IHttpClientFactory _httpFactory = null!;
    private RecordingHttpMessageHandler _handler = null!;
    private IEventBus _eventBus = null!;
    private TestTimeProvider _clock = null!;
    private YouTubeOAuthOptions _options = null!;
    private YouTubeOAuthFlowCoordinator _flow = null!;
    private YouTubeOAuthTokenRefresher _refresher = null!;
    private YouTubeOAuthService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "youtube-oauth-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settingsStore = new(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_tempDir, "youtube.json"));
        _settingsStore.Save(new()
        {
            Enabled = true,
            ProjectId = "yt-proj-1",
            ClientId = "yt-client-id.apps.googleusercontent.com",
            DailyQuotaBudget = 10_000,
        });

        _accountsStore = new(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_tempDir, "youtube-accounts.json"));
        _accountsStore.SaveClientSecret("yt-client-secret");

        _httpFactory = Substitute.For<IHttpClientFactory>();
        _handler = new();
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new(_handler, disposeHandler: false));

        _eventBus = Substitute.For<IEventBus>();
        _clock = new() { UtcNow = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero) };
        _options = new()
        {
            AuthorizationEndpoint = "http://auth.local/o/oauth2/v2/auth",
            TokenEndpoint = "http://token.local/token",
            ChannelsEndpoint = "http://api.local/youtube/v3/channels",
            AuthTimeout = TimeSpan.FromSeconds(15),
            CredentialsRejectedCooldown = TimeSpan.FromMinutes(10),
            TokenRefreshMargin = TimeSpan.FromMinutes(5),
        };

        var statusReporter = new YouTubeOAuthStatusReporter(NullLogger<YouTubeOAuthStatusReporter>.Instance);
        var tokenClient = new YouTubeOAuthTokenClient(_httpFactory, _options, NullLogger<YouTubeOAuthTokenClient>.Instance);
        var channelIdentityClient = new YouTubeChannelIdentityClient(_httpFactory, _options, NullLogger<YouTubeChannelIdentityClient>.Instance);
        var accountWriter = new YouTubeOAuthAccountWriter(_accountsStore, _eventBus, statusReporter, _clock, NullLogger<YouTubeOAuthAccountWriter>.Instance);
        _flow = new(tokenClient, channelIdentityClient, accountWriter, statusReporter, _accountsStore, _settingsStore, _options, NullLogger<YouTubeOAuthFlowCoordinator>.Instance);
        _refresher = new(tokenClient, accountWriter, statusReporter, _accountsStore, _settingsStore, _options, _eventBus, _clock, NullLogger<YouTubeOAuthTokenRefresher>.Instance);
        _service = new(_flow, _refresher, accountWriter, statusReporter);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _refresher.Dispose();
        _flow.Dispose();
        _handler.Dispose();

        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Test]
    public async Task StartOAuthFlowAsync_RunsLoopbackPkceFlow_AndPersistsTokensAndChannelInfo()
    {
        _handler.Responder = (request, body) =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith(_options.TokenEndpoint, StringComparison.Ordinal))
            {
                return MakeJson(HttpStatusCode.OK, """
                    {
                      "access_token": "yt-access-1",
                      "refresh_token": "yt-refresh-1",
                      "expires_in": 3600,
                      "scope": "https://www.googleapis.com/auth/youtube.force-ssl"
                    }
                    """);
            }

            if (request.RequestUri.AbsoluteUri.StartsWith(_options.ChannelsEndpoint, StringComparison.Ordinal))
            {
                Assert.That(request.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
                Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo("yt-access-1"));

                return MakeJson(HttpStatusCode.OK, """
                    {
                      "items": [
                        { "id": "UCbot123", "snippet": { "title": "MyBotChannel" } }
                      ]
                    }
                    """);
            }

            return new(HttpStatusCode.NotFound);
        };

        string? capturedAuthUrl = null;
        HttpResponseMessage? browserResponse = null;
        Task? browserTask = null;

        var flowResult = await _service.StartOAuthFlowAsync(
            PlatformAuthRole.Bot,
            clientId: string.Empty,
            clientSecret: string.Empty,
            scopes: null,
            onAuthUrlReady: authUrl =>
            {
                capturedAuthUrl = authUrl;
                browserTask = Task.Run(async () =>
                {
                    var query = QueryHelpers.ParseQuery(new Uri(authUrl).Query);
                    var redirectUri = query["redirect_uri"].ToString();
                    var state = query["state"].ToString();

                    using var browserClient = new HttpClient();
                    browserResponse = await browserClient.GetAsync($"{redirectUri}?code=auth-code-xyz&state={Uri.EscapeDataString(state)}");
                });
            });

        if (browserTask is not null)
        {
            await browserTask;
        }

        Assert.That(capturedAuthUrl, Is.Not.Null);
        var authQuery = QueryHelpers.ParseQuery(new Uri(capturedAuthUrl!).Query);
        var codeChallenge = authQuery["code_challenge"].ToString();
        var redirectParam = authQuery["redirect_uri"].ToString();

        var tokenRequest = _handler.Requests.Single(r => r.Uri.AbsoluteUri.StartsWith(_options.TokenEndpoint, StringComparison.Ordinal));
        var tokenForm = QueryHelpers.ParseQuery("?" + tokenRequest.Body);
        var sentVerifier = tokenForm["code_verifier"].ToString();
        var expectedChallenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(sentVerifier)));

        var botAccount = _accountsStore.LoadBot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(authQuery["client_id"].ToString(), Is.EqualTo("yt-client-id.apps.googleusercontent.com"));
            Assert.That(authQuery["code_challenge_method"].ToString(), Is.EqualTo("S256"));
            Assert.That(authQuery["scope"].ToString(), Is.EqualTo(YouTubeScopes.ForceSsl));
            Assert.That(authQuery["prompt"].ToString(), Is.EqualTo("select_account consent"));
            Assert.That(redirectParam, Does.StartWith("http://127.0.0.1:"));
            Assert.That(expectedChallenge, Is.EqualTo(codeChallenge), "Отправленный в token endpoint code_verifier должен соответствовать code_challenge из browser URL");
            Assert.That(tokenForm["client_secret"].ToString(), Is.EqualTo("yt-client-secret"));
            Assert.That(tokenForm["code"].ToString(), Is.EqualTo("auth-code-xyz"));
            Assert.That(tokenForm["redirect_uri"].ToString(), Is.EqualTo(redirectParam));

            Assert.That(browserResponse?.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(flowResult.ChannelId, Is.EqualTo("UCbot123"));
            Assert.That(botAccount.AccessToken, Is.EqualTo("yt-access-1"));
            Assert.That(botAccount.RefreshToken, Is.EqualTo("yt-refresh-1"));
            Assert.That(botAccount.ChannelTitle, Is.EqualTo("MyBotChannel"));
            Assert.That(botAccount.AccessTokenExpiresAt, Is.EqualTo(_clock.UtcNow.AddSeconds(3600)));
        }

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<PlatformAuthorizationRefreshed>(e => e.Role == PlatformAuthRole.Bot), Arg.Any<CancellationToken>());
    }

    [Test]
    public void FlowResult_ApplyTo_BindsPreservesAndClearsBrowserProfileName()
    {
        var account = new YouTubeAccountSettings
        {
            ChannelId = "UC1",
            BrowserProfileName = "existingprofile",
        };
        var now = _clock.UtcNow;

        new YouTubeOAuthFlowResult("at1", "rt1", [YouTubeScopes.ForceSsl], "UC1", "Chan1", 3600)
            .ApplyTo(account, now);
        Assert.That(account.BrowserProfileName, Is.EqualTo("existingprofile"));

        new YouTubeOAuthFlowResult("at2", "rt2", [YouTubeScopes.ForceSsl], "UC1", "Chan1", 3600)
            { BrowserProfileName = "embeddedprofile" }
            .ApplyTo(account, now);
        Assert.That(account.BrowserProfileName, Is.EqualTo("embeddedprofile"));

        new YouTubeOAuthFlowResult("at3", "rt3", [YouTubeScopes.ForceSsl], "UC2", "Chan2", 3600)
            .ApplyTo(account, now);
        Assert.That(account.BrowserProfileName, Is.Null);
    }

    [Test]
    public async Task StartOAuthFlowToDraftAsync_DoesNotSaveOrPublishEvent()
    {
        _handler.Responder = (request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith(_options.TokenEndpoint, StringComparison.Ordinal))
            {
                return MakeJson(HttpStatusCode.OK, """
                    {
                      "access_token": "draft-access",
                      "refresh_token": "draft-refresh",
                      "expires_in": 1800,
                      "scope": "https://www.googleapis.com/auth/youtube.force-ssl"
                    }
                    """);
            }

            return MakeJson(HttpStatusCode.OK, """
                { "items": [ { "id": "UCcaster", "snippet": { "title": "StreamerChan" } } ] }
                """);
        };

        var draft = await _service.StartOAuthFlowToDraftAsync(
            PlatformAuthRole.Broadcaster,
            "override-client-id",
            "override-secret",
            null,
            onAuthUrlReady: authUrl =>
            {
                _ = Task.Run(async () =>
                {
                    var query = QueryHelpers.ParseQuery(new Uri(authUrl).Query);
                    using var browserClient = new HttpClient();
                    await browserClient.GetAsync($"{query["redirect_uri"]}?code=c1&state={Uri.EscapeDataString(query["state"].ToString())}");
                });
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft.AccessToken, Is.EqualTo("draft-access"));
            Assert.That(draft.ChannelId, Is.EqualTo("UCcaster"));
            Assert.That(draft.Scopes, Is.EqualTo(new[] { YouTubeScopes.ForceSsl }));
            Assert.That(_accountsStore.LoadBroadcaster().AccessToken, Is.Empty);
        }

        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync(default(PlatformAuthorizationRefreshed)!, default);
    }

    [Test]
    public async Task StartOAuthFlowAsync_WhenStateMismatches_ThrowsAndServesErrorPage()
    {
        HttpStatusCode? browserStatus = null;
        Task? browserTask = null;

        var exception = Assert.ThrowsAsync<YouTubeOAuthException>(() =>
            _service.StartOAuthFlowAsync(
                PlatformAuthRole.Bot,
                string.Empty,
                string.Empty,
                null,
                authUrl =>
                {
                    browserTask = Task.Run(async () =>
                    {
                        var query = QueryHelpers.ParseQuery(new Uri(authUrl).Query);
                        using var browserClient = new HttpClient();
                        var response = await browserClient.GetAsync($"{query["redirect_uri"]}?code=c1&state=tampered-state");
                        browserStatus = response.StatusCode;
                    });
                }));

        if (browserTask is not null)
        {
            await browserTask;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception?.ErrorCode, Is.EqualTo("state_mismatch"));
            Assert.That(browserStatus, Is.EqualTo(HttpStatusCode.BadRequest));
        }
    }

    [Test]
    public async Task StartOAuthFlowAsync_WhenUserDeniesConsent_ThrowsYouTubeOAuthException()
    {
        HttpStatusCode? browserStatus = null;
        Task? browserTask = null;

        var exception = Assert.ThrowsAsync<YouTubeOAuthException>(() =>
            _service.StartOAuthFlowAsync(
                PlatformAuthRole.Bot,
                string.Empty,
                string.Empty,
                null,
                authUrl =>
                {
                    browserTask = Task.Run(async () =>
                    {
                        var query = QueryHelpers.ParseQuery(new Uri(authUrl).Query);
                        using var browserClient = new HttpClient();
                        var response = await browserClient.GetAsync($"{query["redirect_uri"]}?error=access_denied&state={Uri.EscapeDataString(query["state"].ToString())}");
                        browserStatus = response.StatusCode;
                    });
                }));

        if (browserTask is not null)
        {
            await browserTask;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception?.ErrorCode, Is.EqualTo("access_denied"));
            Assert.That(browserStatus, Is.EqualTo(HttpStatusCode.BadRequest));
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenExpired_PreservesExistingRefreshTokenIfOmitted()
    {
        _accountsStore.Mutate(PlatformAuthRole.Broadcaster, account =>
        {
            account.AccessToken = "old-access";
            account.RefreshToken = "keep-this-refresh";
            account.ChannelId = "UCcaster";
            account.ChannelTitle = "StreamerChan";
            account.AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(1);
        });

        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.OK, """
            {
              "access_token": "fresh-access",
              "expires_in": 3600
            }
            """);

        var token = await _service.GetAccessTokenAsync(PlatformAuthRole.Broadcaster);
        var updated = _accountsStore.LoadBroadcaster();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(token, Is.EqualTo("fresh-access"));
            Assert.That(updated.AccessToken, Is.EqualTo("fresh-access"));
            Assert.That(updated.RefreshToken, Is.EqualTo("keep-this-refresh"));
            Assert.That(updated.AccessTokenExpiresAt, Is.EqualTo(_clock.UtcNow.AddSeconds(3600)));
        }

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<PlatformAuthorizationRefreshed>(e => e.Role == PlatformAuthRole.Broadcaster), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenGoogleReturnsInvalidGrant_ClearsAccountTokensAndRespectsCooldown()
    {
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "expired";
            account.RefreshToken = "revoked-refresh";
            account.ChannelId = "UCbot";
            account.ChannelTitle = "BotChan";
            account.AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(-10);
        });

        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.BadRequest, """
            {
              "error": "invalid_grant",
              "error_description": "Token has been expired or revoked."
            }
            """);

        var first = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);
        var second = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);
        var account = _accountsStore.LoadBot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(account.AccessToken, Is.Empty);
            Assert.That(account.RefreshToken, Is.Empty);
            Assert.That(account.ChannelId, Is.Empty);
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_OnTransientError_EntersBackoffAndRecoversAfterDelay()
    {
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "expired";
            account.RefreshToken = "valid-refresh";
            account.AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(-10);
        });

        var call = 0;
        _handler.Responder = (_, _) =>
        {
            call++;
            return call == 1
                ? new(HttpStatusCode.ServiceUnavailable)
                : MakeJson(HttpStatusCode.OK, """{ "access_token": "recovered-access", "expires_in": 3600 }""");
        };

        var failed = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);
        var backedOff = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);
        var recovered = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Is.Null);
            Assert.That(backedOff, Is.Null);
            Assert.That(recovered, Is.EqualTo("recovered-access"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task StartOAuthFlowAsync_IgnoresIdleAndResetLoopbackConnections_ThenCompletesOnValidCallback()
    {
        var fastTimeoutOptions = new YouTubeOAuthOptions
        {
            AuthorizationEndpoint = _options.AuthorizationEndpoint,
            TokenEndpoint = _options.TokenEndpoint,
            ChannelsEndpoint = _options.ChannelsEndpoint,
            AuthTimeout = TimeSpan.FromSeconds(10),
            LoopbackConnectionTimeout = TimeSpan.FromMilliseconds(150),
            CredentialsRejectedCooldown = _options.CredentialsRejectedCooldown,
            TokenRefreshMargin = _options.TokenRefreshMargin,
        };

        var statusReporter = new YouTubeOAuthStatusReporter(NullLogger<YouTubeOAuthStatusReporter>.Instance);
        var tokenClient = new YouTubeOAuthTokenClient(_httpFactory, fastTimeoutOptions, NullLogger<YouTubeOAuthTokenClient>.Instance);
        var channelIdentityClient = new YouTubeChannelIdentityClient(_httpFactory, fastTimeoutOptions, NullLogger<YouTubeChannelIdentityClient>.Instance);
        var accountWriter = new YouTubeOAuthAccountWriter(_accountsStore, _eventBus, statusReporter, _clock, NullLogger<YouTubeOAuthAccountWriter>.Instance);
        using var coordinator = new YouTubeOAuthFlowCoordinator(tokenClient, channelIdentityClient, accountWriter, statusReporter, _accountsStore, _settingsStore, fastTimeoutOptions, NullLogger<YouTubeOAuthFlowCoordinator>.Instance);

        _handler.Responder = (request, _) => request.RequestUri!.AbsoluteUri.StartsWith(fastTimeoutOptions.TokenEndpoint, StringComparison.Ordinal)
            ? MakeJson(HttpStatusCode.OK, """{ "access_token": "acc-idle", "refresh_token": "ref-idle", "expires_in": 3600, "scope": "https://www.googleapis.com/auth/youtube.force-ssl" }""")
            : MakeJson(HttpStatusCode.OK, """{ "items": [ { "id": "UCidle", "snippet": { "title": "IdleChannel" } } ] }""");

        Uri? capturedAuthUri = null;
        using var urlReady = new SemaphoreSlim(0, 1);

        var flowTask = coordinator.StartOAuthFlowAsync(
            PlatformAuthRole.Bot,
            "yt-client-id.apps.googleusercontent.com",
            "yt-client-secret",
            null,
            url =>
            {
                capturedAuthUri = new(url);
                urlReady.Release();
            });

        await urlReady.WaitAsync(TimeSpan.FromSeconds(5));
        var query = QueryHelpers.ParseQuery(capturedAuthUri!.Query);
        var redirectUri = new Uri(query["redirect_uri"].ToString());
        var state = query["state"].ToString();

        using (var immediateClose = new TcpClient())
        {
            await immediateClose.ConnectAsync(IPAddress.Loopback, redirectUri.Port);
        }

        using (var idleSocket = new TcpClient())
        {
            await idleSocket.ConnectAsync(IPAddress.Loopback, redirectUri.Port);
            await Task.Delay(250);
        }

        using var http = new HttpClient();
        var callbackResponse = await http.GetAsync($"{redirectUri}?code=idle-ok&state={Uri.EscapeDataString(state)}");
        var result = await flowTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(callbackResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.AccessToken, Is.EqualTo("acc-idle"));
            Assert.That(result.ChannelId, Is.EqualTo("UCidle"));
        }
    }

    [Test]
    public void AddYouTubeClients_ResolvesServiceFromDiContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSettingsStores();
        services.AddSingleton(_eventBus);
        services.AddYouTubeClients();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IYouTubeOAuthService>();

        Assert.That(resolved, Is.TypeOf<YouTubeOAuthService>());
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenCredentialsRejectedAndClientSecretUpdated_ResetsCooldownImmediately()
    {
        _accountsStore.SaveClientSecret("wrong-secret");
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = string.Empty;
            account.RefreshToken = "yt-refresh";
            account.StoredScopes = [..YouTubeScopes.BotRequired];
        });

        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"Unauthorized"}""");
        var first = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        _accountsStore.SaveClientSecret("good-secret");
        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.OK, """{"access_token":"recovered-access","expires_in":3600}""");
        var recovered = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(recovered, Is.EqualTo("recovered-access"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenCredentialsRejectedAndAuthorizationRefreshedEventPublished_ResetsCooldown()
    {
        var realBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var statusReporter = new YouTubeOAuthStatusReporter(NullLogger<YouTubeOAuthStatusReporter>.Instance);
        var tokenClient = new YouTubeOAuthTokenClient(_httpFactory, _options, NullLogger<YouTubeOAuthTokenClient>.Instance);
        var channelIdentityClient = new YouTubeChannelIdentityClient(_httpFactory, _options, NullLogger<YouTubeChannelIdentityClient>.Instance);
        var accountWriter = new YouTubeOAuthAccountWriter(_accountsStore, realBus, statusReporter, _clock, NullLogger<YouTubeOAuthAccountWriter>.Instance);
        using var coordinator = new YouTubeOAuthFlowCoordinator(tokenClient, channelIdentityClient, accountWriter, statusReporter, _accountsStore, _settingsStore, _options, NullLogger<YouTubeOAuthFlowCoordinator>.Instance);
        using var refresher = new YouTubeOAuthTokenRefresher(tokenClient, accountWriter, statusReporter, _accountsStore, _settingsStore, _options, realBus, _clock, NullLogger<YouTubeOAuthTokenRefresher>.Instance);
        using var service = new YouTubeOAuthService(coordinator, refresher, accountWriter, statusReporter);

        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = string.Empty;
            account.RefreshToken = "yt-refresh";
            account.StoredScopes = [..YouTubeScopes.BotRequired];
        });

        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        Assert.That(await service.GetAccessTokenAsync(PlatformAuthRole.Bot), Is.Null);

        await realBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));

        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.OK, """{"access_token":"after-event","expires_in":3600}""");
        var recovered = await service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        Assert.That(recovered, Is.EqualTo("after-event"));
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenCredentialsRejected_StatusReporterDoesNotLeakRawErrorDescription()
    {
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "expired";
            account.RefreshToken = "refresh-token";
            account.AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(-5);
            account.StoredScopes = [..YouTubeScopes.BotRequired];
        });

        string? reportedStatus = null;
        _service.StatusChanged += (_, message) => reportedStatus = message;

        _handler.Responder = (_, _) => MakeJson(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"RAW_GOOGLE_SECRET_DETAILS"}""");

        var token = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);

        Assert.That(token, Is.Null);
        Assert.That(reportedStatus, Is.Not.Null);
        Assert.That(reportedStatus, Does.Not.Contain("RAW_GOOGLE_SECRET_DETAILS"));
        Assert.That(reportedStatus, Does.Contain("неверный Client ID или Client Secret Google-проекта"));
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenStoredScopesMissingRequiredScope_RefusesAndClearsAccessToken()
    {
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "present-but-under-scoped";
            account.RefreshToken = string.Empty;
            account.AccessTokenExpiresAt = _clock.UtcNow.AddHours(1);
            account.StoredScopes = [YouTubeScopes.ReadOnly];
        });

        var token = await _service.GetAccessTokenAsync(PlatformAuthRole.Bot);
        var stored = _accountsStore.Load(PlatformAuthRole.Bot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(token, Is.Null);
            Assert.That(stored.AccessToken, Is.Empty);
            Assert.That(stored.AccessTokenExpiresAt, Is.Null);
            Assert.That(stored.StoredScopes, Is.Empty);
            Assert.That(_service.HasUsableAccessToken(PlatformAuthRole.Bot), Is.False);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_WhenBroadcasterHasLegacyReadOnlyScope_KeepsTokenUsable()
    {
        _accountsStore.Mutate(PlatformAuthRole.Broadcaster, account =>
        {
            account.AccessToken = "legacy-readonly-token";
            account.RefreshToken = "legacy-refresh";
            account.AccessTokenExpiresAt = _clock.UtcNow.AddHours(1);
            account.Scopes = [YouTubeScopes.ReadOnly];
            account.StoredScopes = [YouTubeScopes.ReadOnly];
        });

        var token = await _service.GetAccessTokenAsync(PlatformAuthRole.Broadcaster);

        Assert.That(token, Is.EqualTo("legacy-readonly-token"));
    }

    [TestCase("accessNotConfigured", true)]
    [TestCase("quotaExceeded", false)]
    [TestCase("youtubeSignupRequired", true)]
    public void GetOwnChannelAsync_OnGoogleErrorReason_ThrowsTypedExceptionWithExpectedPermanentFlag(string reason, bool expectedPermanent)
    {
        var client = new YouTubeChannelIdentityClient(_httpFactory, _options, NullLogger<YouTubeChannelIdentityClient>.Instance);
        _handler.Responder = (_, _) => MakeJson(HttpStatusCode.Forbidden,
            $$$"""{"error":{"code":403,"errors":[{"reason":"{{{reason}}}"}]}}""");

        var ex = Assert.ThrowsAsync<YouTubeOAuthException>(async () => await client.GetOwnChannelAsync("access-token"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.ErrorCode, Is.EqualTo(reason));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(ex.IsPermanentRejection, Is.EqualTo(expectedPermanent));
        }
    }

    [Test]
    public async Task StartOAuthFlowToDraftAsync_WhenConcurrentFlowInProgressForSameRole_NotifiesBusyAndWaits()
    {
        using var firstUrlReady = new ManualResetEventSlim();
        using var secondUrlReady = new ManualResetEventSlim();
        using var busyReported = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _service.StatusChanged += (role, message) =>
        {
            if (role == PlatformAuthRole.Bot && message.Contains("уже идёт авторизация", StringComparison.Ordinal))
            {
                busyReported.Set();
            }
        };

        var first = _flow.StartOAuthFlowToDraftAsync(
            PlatformAuthRole.Bot,
            "yt-client-id",
            "yt-client-secret",
            null,
            _ => firstUrlReady.Set(),
            cts.Token);

        Assert.That(firstUrlReady.Wait(TimeSpan.FromSeconds(3)), Is.True);

        var second = _flow.StartOAuthFlowToDraftAsync(
            PlatformAuthRole.Bot,
            "yt-client-id",
            "yt-client-secret",
            null,
            _ => secondUrlReady.Set(),
            cts.Token);

        Assert.That(busyReported.Wait(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(secondUrlReady.IsSet, Is.False);

        await cts.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(async () => await first);
        Assert.CatchAsync<OperationCanceledException>(async () => await second);
    }

    [Test]
    public void YouTubeScopes_IsScopeSatisfied_TreatsForceSslAsSatisfyingReadOnly()
    {
        var forceSslOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { YouTubeScopes.ForceSsl };
        var readOnlyOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { YouTubeScopes.ReadOnly };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(YouTubeScopes.IsScopeSatisfied(forceSslOnly, YouTubeScopes.ForceSsl), Is.True);
            Assert.That(YouTubeScopes.IsScopeSatisfied(forceSslOnly, YouTubeScopes.ReadOnly), Is.True);
            Assert.That(YouTubeScopes.IsScopeSatisfied(readOnlyOnly, YouTubeScopes.ReadOnly), Is.True);
            Assert.That(YouTubeScopes.IsScopeSatisfied(readOnlyOnly, YouTubeScopes.ForceSsl), Is.False);
        }
    }

    private static HttpResponseMessage MakeJson(HttpStatusCode status, string body)
    {
        return new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record RecordedRequest(Uri Uri, string Body);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, string, HttpResponseMessage>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new(request.RequestUri!, body));
            return Responder?.Invoke(request, body) ?? new(HttpStatusCode.NotImplemented);
        }
    }
}
