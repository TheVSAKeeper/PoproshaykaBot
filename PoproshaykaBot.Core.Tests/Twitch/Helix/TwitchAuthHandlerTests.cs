using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.Core.Tests.Twitch.Helix;

[TestFixture]
public sealed class TwitchAuthHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-auth-handler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _accountsStore = new(filePath: Path.Combine(_tempDir, "accounts.json"));
        _accountsStore.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.Login = "thebot";
            account.UserId = "42";
        });

        _oauthService = Substitute.For<ITwitchOAuthService>();

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settings = new()
        {
            Twitch =
            {
                ClientId = "test-client-id",
            },
        };

        _settingsManager.Current.Returns(_settings);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string _tempDir = null!;
    private AccountsStore _accountsStore = null!;
    private ITwitchOAuthService _oauthService = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;

    [Test]
    public async Task Broadcaster_uses_the_active_catalog_context_and_explicit_contexts_take_precedence()
    {
        const PlatformAuthRole Role = PlatformAuthRole.Broadcaster;
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json"));
        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("legacy", "legacy", false, true, true));
        var channels = new PlatformChannelRegistry();
        var authorization = new TwitchChannelAuthorization(_accountsStore, catalog, channels, target);

        var a = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.Twitch, ChannelId = "10", IsConfigured = true,
            BroadcasterContextId = "owner-a", BroadcasterAccountId = "10", BroadcasterVerifiedSubjectId = "10",
        };
        var b = new ChannelCatalogEntry
        {
            Platform = a.Platform, ChannelId = "20", IsConfigured = true,
            BroadcasterContextId = "owner-b", BroadcasterAccountId = "20", BroadcasterVerifiedSubjectId = "20",
        };
        _accountsStore.UpsertContext(Role, "owner-a", "A", new() { UserId = "10", AccessToken = "token-a" });
        _accountsStore.UpsertContext(Role, "owner-b", "B", new() { UserId = "20", AccessToken = "token-b" });
        _accountsStore.Mutate(Role, account => account.AccessToken = "legacy-token");
        _oauthService.GetAccessTokenAsync(Role, "owner-a", Arg.Any<CancellationToken>()).Returns("token-a");
        _oauthService.GetAccessTokenAsync(Role, "owner-b", Arg.Any<CancellationToken>()).Returns("token-b");
        _oauthService.GetAccessTokenAsync(Role, Arg.Any<CancellationToken>()).Returns("legacy-token");
        catalog.Save(new() { Channels = [a, b], Platforms = [new() { Platform = a.Platform, ActiveChannelId = a.ChannelId }] });

        var stub = new StubHttpMessageHandler { Responder = _ => new(HttpStatusCode.OK) };
        using var client = new HttpClient(new BroadcasterTwitchAuthHandler(_oauthService, _settingsManager, _accountsStore,
            NullLogger<BroadcasterTwitchAuthHandler>.Instance, authorization) { InnerHandler = stub })
        {
            BaseAddress = new("https://api.twitch.tv/"),
        };

        using var first = await client.GetAsync("helix/polls");
        channels.SetBinding(new(b.Key, "B", "default", "owner-b", 1));
        using var second = await client.GetAsync("helix/polls");
        using var request = new HttpRequestMessage(HttpMethod.Get, "helix/polls");
        request.Options.Set(PlatformAuthRequestOptions.ContextIdKey, "default");
        using var explicitDefault = await client.SendAsync(request);
        catalog.Mutate(settings => settings.Channels.Single(entry => entry.ChannelId == b.ChannelId).BroadcasterVerifiedSubjectId = null);

        Assert.ThrowsAsync<TwitchAuthorizationMissingException>(async () => await client.GetAsync("helix/polls"));
        catalog.Mutate(settings => settings.Channels.Single(entry => entry.ChannelId == b.ChannelId).BroadcasterAccountId = null);
        Assert.ThrowsAsync<TwitchAuthorizationMissingException>(async () => await client.GetAsync("helix/polls"));
        Assert.That(stub.Requests.Select(item => item.Headers.Authorization?.Parameter),
            Is.EqualTo(new[] { "token-a", "token-b", "legacy-token" }));
    }

    private (HttpClient client, StubHttpMessageHandler stub) BuildPipeline()
    {
        var stub = new StubHttpMessageHandler();
        var auth = new BotTwitchAuthHandler(_oauthService, _settingsManager, _accountsStore,
            NullLogger<BotTwitchAuthHandler>.Instance)
        {
            InnerHandler = stub,
        };

        var client = new HttpClient(auth) { BaseAddress = new("https://api.twitch.tv/") };
        return (client, stub);
    }

    [Test]
    public async Task SendAsync_AddsBearerAndClientIdHeaders_WhenTokenAvailable()
    {
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns("valid-token");

        var (client, stub) = BuildPipeline();
        stub.Responder = _ => StubHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, "{}").Responder!.Invoke(_);

        await client.GetAsync("helix/users");

        var request = stub.Requests[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo("valid-token"));
            Assert.That(request.Headers.GetValues("Client-Id"), Is.EquivalentTo(["test-client-id"]),
                "Client-Id обязателен для всех Helix-запросов");
        }
    }

    [Test]
    public void SendAsync_ThrowsWhenOAuthServiceReturnsNullToken()
    {
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var (client, _) = BuildPipeline();

        Assert.ThrowsAsync<TwitchAuthorizationMissingException>(async () => await client.GetAsync("helix/users"));
    }

    [Test]
    public void SendAsync_ThrowsWhenOAuthServiceReturnsBlankToken()
    {
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns("   ");

        var (client, _) = BuildPipeline();

        Assert.ThrowsAsync<TwitchAuthorizationMissingException>(async () => await client.GetAsync("helix/users"));
    }

    [Test]
    public async Task SendAsync_On401_ClearsStoredAccessToken_WhenStillCurrent()
    {
        const string Token = "stored-token";
        _accountsStore.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = Token);
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns(Token);

        var (client, stub) = BuildPipeline();
        stub.Responder = _ => new(HttpStatusCode.Unauthorized);

        var response = await client.GetAsync("helix/users");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "Auth-handler не глотает 401 – пробрасывает caller-у, чтобы тот видел и принимал решение");

            Assert.That(_accountsStore.LoadBot().AccessToken, Is.Empty,
                "Сохранённый токен должен быть очищен – следующий вызов должен пройти через TwitchOAuthService");
        }
    }

    [Test]
    public async Task SendAsync_On401_DoesNotClearTokenWhenItChangedConcurrently()
    {
        const string OldToken = "old-token";
        const string NewToken = "new-token";

        _accountsStore.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = NewToken);

        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns(OldToken);

        var (client, stub) = BuildPipeline();
        stub.Responder = _ => new(HttpStatusCode.Unauthorized);

        await client.GetAsync("helix/users");

        Assert.That(_accountsStore.LoadBot().AccessToken, Is.EqualTo(NewToken),
            "Если токен в хранилище уже сменился (refresh от другого потока), 401 от старого ответа не должен затирать новый");
    }

    [Test]
    public void Отказ_шёпота_по_401_не_стирает_токен_бота()
    {
        const string Token = "stored-token";
        _accountsStore.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = Token);
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns(Token);

        var (client, stub) = BuildPipeline();
        stub.Responder = _ => StubHttpMessageHandler.ReturnsJson(HttpStatusCode.Unauthorized,
                """{"error":"Unauthorized","status":401,"message":"Missing scope: user:manage:whispers"}""")
            .Responder!.Invoke(_);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var helix = new BotHelixClient(factory, NullLogger<BotHelixClient>.Instance);

        var exception = Assert.ThrowsAsync<HelixRequestException>(async () => await helix.SendWhisperAsync("42", "7", "hi"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(exception.TwitchErrorMessage, Is.EqualTo("Missing scope: user:manage:whispers"));
            Assert.That(stub.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(stub.Requests[0].RequestUri!.PathAndQuery, Is.EqualTo("/helix/whispers?from_user_id=42&to_user_id=7"));
            Assert.That(stub.RequestBodies[0], Is.EqualTo("""{"message":"hi"}"""));

            Assert.That(_accountsStore.LoadBot().AccessToken, Is.EqualTo(Token),
                "401 шёпота – это нет права или телефона у бота, а не негодный токен: иначе откат в реплай ушёл бы уже без авторизации");
        }
    }

    [Test]
    public async Task SendAsync_On200_DoesNotTouchAccountsStore()
    {
        const string Token = "valid-token";
        _accountsStore.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = Token);
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns(Token);

        var (client, stub) = BuildPipeline();
        stub.Responder = _ => StubHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, "{}").Responder!.Invoke(_);

        await client.GetAsync("helix/users");

        Assert.That(_accountsStore.LoadBot().AccessToken, Is.EqualTo(Token),
            "Успешный ответ не должен дёргать TryClearAccessToken – токен валиден");
    }

    [Test]
    public async Task SendAsync_BroadcasterHandler_RoutesThroughBroadcasterRole()
    {
        _accountsStore.Mutate(PlatformAuthRole.Broadcaster, account => account.Login = "thecaster");
        _oauthService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, Arg.Any<CancellationToken>())
            .Returns("broadcaster-token");

        var stub = new StubHttpMessageHandler
        {
            Responder = _ => StubHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, "{}").Responder!.Invoke(_),
        };

        var auth = new BroadcasterTwitchAuthHandler(_oauthService, _settingsManager, _accountsStore,
            NullLogger<BroadcasterTwitchAuthHandler>.Instance)
        {
            InnerHandler = stub,
        };

        using var client = new HttpClient(auth) { BaseAddress = new("https://api.twitch.tv/") };

        await client.GetAsync("helix/channels");

        await _oauthService.Received().GetAccessTokenAsync(PlatformAuthRole.Broadcaster, Arg.Any<CancellationToken>());
        Assert.That(stub.Requests[0].Headers.Authorization?.Parameter, Is.EqualTo("broadcaster-token"));
    }
}
