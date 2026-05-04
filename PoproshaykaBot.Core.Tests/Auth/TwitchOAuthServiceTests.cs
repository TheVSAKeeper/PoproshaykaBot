using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Tests.Auth;

[TestFixture]
public sealed class TwitchOAuthServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "twitch-oauth-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings = new();
        _settings.Twitch.ClientId = "test-client-id";
        _settings.Twitch.ClientSecret = "test-client-secret";
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);
        _settingsManager.Current.Returns(_settings);
        _accountsStore = new(filePath: Path.Combine(_tempDir, "accounts.json"));

        _httpFactory = Substitute.For<IHttpClientFactory>();
        _handler = new();
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new(_handler, false));
        _eventBus = Substitute.For<IEventBus>();

        _service = new(_settingsManager, _accountsStore, _httpFactory, NullLogger<TwitchOAuthService>.Instance, _eventBus);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _handler.Dispose();
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string _tempDir = null!;
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private AccountsStore _accountsStore = null!;
    private IHttpClientFactory _httpFactory = null!;
    private FakeHttpMessageHandler _handler = null!;
    private IEventBus _eventBus = null!;
    private TwitchOAuthService _service = null!;

    [Test]
    public async Task UpdateSettings_DefaultsToNotPublishingAuthorizationRefreshed()
    {
        _service.UpdateSettings(TwitchOAuthRole.Bot,
            "access-token",
            "refresh-token",
            null,
            "bot-login",
            "user-id");

        await _eventBus.DidNotReceive()
            .PublishAsync(Arg.Any<TwitchAuthorizationRefreshed>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateSettings_WithFlag_PublishesAuthorizationRefreshedForRole()
    {
        _service.UpdateSettings(TwitchOAuthRole.Bot,
            "access-token",
            "refresh-token",
            null,
            "bot-login",
            "user-id",
            publishAuthorizationRefreshed: true);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<TwitchAuthorizationRefreshed>(e => e.Role == TwitchOAuthRole.Bot),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateSettings_WithFlagForBroadcaster_PublishesEventForBroadcaster()
    {
        _service.UpdateSettings(TwitchOAuthRole.Broadcaster,
            "access-token",
            "refresh-token",
            null,
            "streamer-login",
            "user-id",
            publishAuthorizationRefreshed: true);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<TwitchAuthorizationRefreshed>(e => e.Role == TwitchOAuthRole.Broadcaster),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAccessToken_WhenRefreshRejectedWith400_ClearsTokensAndPublishesStatusAndDoesNotLoopOnNextCall()
    {
        SeedExpiredBotTokens();
        _handler.Responder = _ => MakeJson(HttpStatusCode.BadRequest,
            "{\"status\":400,\"message\":\"Invalid refresh token\"}");

        var token = await _service.GetAccessTokenAsync(TwitchOAuthRole.Bot);

        Assert.That(token, Is.Null);

        var bot = _accountsStore.LoadBot();
        Assert.That(bot.AccessToken, Is.Empty);
        Assert.That(bot.RefreshToken, Is.Empty);
        Assert.That(bot.AccessTokenExpiresAt, Is.Null);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<BotConnectionStatusUpdated>(e => e.Message.Contains("повторная авторизация")),
                Arg.Any<CancellationToken>());

        var requestsAfterFirst = _handler.Requests.Count;
        Assert.That(requestsAfterFirst, Is.EqualTo(1));

        var second = await _service.GetAccessTokenAsync(TwitchOAuthRole.Bot);

        Assert.That(second, Is.Null);
        Assert.That(_handler.Requests.Count, Is.EqualTo(requestsAfterFirst),
            "Повторный GetAccessTokenAsync не должен снова идти в Twitch — токены очищены");
    }

    [Test]
    public void RefreshTokenAsync_WhenServerReturns400_RethrowsRejectionAndClearsTokens()
    {
        SeedExpiredBotTokens();
        _handler.Responder = _ => MakeJson(HttpStatusCode.BadRequest,
            "{\"status\":400,\"message\":\"Invalid refresh token\"}");

        Assert.ThrowsAsync<OAuthRefreshRejectedException>(async () =>
            await _service.RefreshTokenAsync(TwitchOAuthRole.Bot,
                _settings.Twitch.ClientId,
                _settings.Twitch.ClientSecret,
                "stale-refresh"));

        var bot = _accountsStore.LoadBot();
        Assert.That(bot.AccessToken, Is.Empty);
        Assert.That(bot.RefreshToken, Is.Empty);
    }

    [Test]
    public async Task GetAccessToken_WhenRefreshFailsWith500_KeepsTokensForLaterRetry()
    {
        SeedExpiredBotTokens();
        _handler.Responder = _ => MakeJson(HttpStatusCode.InternalServerError,
            "{\"status\":500,\"message\":\"transient\"}");

        var token = await _service.GetAccessTokenAsync(TwitchOAuthRole.Bot);

        Assert.That(token, Is.Null);

        var bot = _accountsStore.LoadBot();
        Assert.That(bot.RefreshToken, Is.EqualTo("stale-refresh"),
            "Transient ошибки не должны сжигать refresh-токен");
    }

    private void SeedExpiredBotTokens()
    {
        _accountsStore.Mutate(TwitchOAuthRole.Bot, account =>
        {
            account.AccessToken = "stale-access";
            account.RefreshToken = "stale-refresh";
            account.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            account.Login = "bot-login";
            account.UserId = "bot-id";
        });
    }

    private static HttpResponseMessage MakeJson(HttpStatusCode status, string body)
    {
        return new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotImplemented);
            return Task.FromResult(response);
        }
    }
}
