using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeRestLiveChatSendClientTests
{
    private string _directory = null!;
    private ManualTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-send-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj-1", DailyQuotaBudget = 10_000 });

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings
            {
                AccessToken = "bot-token-1",
                RefreshToken = "bot-refresh-1",
                ChannelId = "UCbot1",
                ChannelTitle = "Bot",
            },
            new YouTubeAccountSettings { ChannelId = "UCbroadcaster1", ChannelTitle = "Broadcaster" });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task InsertMessageAsync_Sends_Expected_Payload_And_Commits_20_Units()
    {
        string? capturedMethod = null;
        string? capturedUri = null;
        string? capturedAuth = null;
        string? capturedBody = null;

        using var handler = new StubHttpHandler(async (request, ct) =>
        {
            capturedMethod = request.Method.Method;
            capturedUri = request.RequestUri?.ToString();
            capturedAuth = request.Headers.Authorization?.ToString();
            capturedBody = request.Content is not null ? await request.Content.ReadAsStringAsync(ct) : null;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"yt-msg-101","snippet":{"publishedAt":"2026-11-16T18:00:05Z"}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = CreateClient(handler);

        var result = await client.InsertMessageAsync("live-chat-1", "Привет, чат!", YouTubeQuotaPriority.CommandReply, CancellationToken.None);
        var diagnostics = _quotaGovernor.CaptureDiagnostics();

        using var doc = JsonDocument.Parse(capturedBody!);
        var snippet = doc.RootElement.GetProperty("snippet");

        Assert.Multiple(() =>
        {
            Assert.That(capturedMethod, Is.EqualTo("POST"));
            Assert.That(capturedUri, Is.EqualTo("https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet"));
            Assert.That(capturedAuth, Is.EqualTo("Bearer bot-token-1"));
            Assert.That(snippet.GetProperty("liveChatId").GetString(), Is.EqualTo("live-chat-1"));
            Assert.That(snippet.GetProperty("type").GetString(), Is.EqualTo("textMessageEvent"));
            Assert.That(snippet.GetProperty("textMessageDetails").GetProperty("messageText").GetString(), Is.EqualTo("Привет, чат!"));
            Assert.That(result.MessageId, Is.EqualTo("yt-msg-101"));
            Assert.That(result.PublishedAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 16, 18, 0, 5, TimeSpan.Zero)));
            Assert.That(diagnostics.CommittedUnits, Is.EqualTo(20));
            Assert.That(diagnostics.BotCommittedUnits, Is.EqualTo(20));
            Assert.That(diagnostics.ReservedUnits, Is.Zero);
        });
    }

    [Test]
    public void InsertMessageAsync_On_401_Clears_Bot_Token_And_Refunds_Quota_Lease()
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"code":401,"errors":[{"reason":"authError"}]}}""", Encoding.UTF8, "application/json"),
        }));

        var client = CreateClient(handler);

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() =>
            client.InsertMessageAsync("live-chat-1", "тест", YouTubeQuotaPriority.CommandReply, CancellationToken.None));

        var diagnostics = _quotaGovernor.CaptureDiagnostics();
        var botAccount = _accountsStore.Load(PlatformAuthRole.Bot);

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Unauthorized));
            Assert.That(botAccount.AccessToken, Is.Empty);
            Assert.That(diagnostics.CommittedUnits, Is.Zero);
            Assert.That(diagnostics.ReservedUnits, Is.Zero);
        });
    }

    [Test]
    public void InsertMessageAsync_On_403_QuotaExceeded_Marks_Governor_Exhausted()
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}""", Encoding.UTF8, "application/json"),
        }));

        var client = CreateClient(handler);

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() =>
            client.InsertMessageAsync("live-chat-1", "тест", YouTubeQuotaPriority.CommandReply, CancellationToken.None));

        var diagnostics = _quotaGovernor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.QuotaExceeded));
            Assert.That(diagnostics.ExhaustedByServer, Is.True);
            Assert.That(diagnostics.CommittedUnits, Is.Zero);
            Assert.That(diagnostics.ReservedUnits, Is.Zero);
        });
    }

    [Test]
    public void InsertMessageAsync_When_Local_Budget_Denied_Throws_Without_Http_Call()
    {
        Assert.That(_quotaGovernor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, 5_500, out var preLease), Is.True);
        preLease.Commit();

        var calls = 0;
        using var handler = new StubHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var client = CreateClient(handler);

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() =>
            client.InsertMessageAsync("live-chat-1", "рассылка", YouTubeQuotaPriority.ScheduledBroadcast, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.LocalQuotaBudgetReached));
            Assert.That(calls, Is.Zero);
            Assert.That(_quotaGovernor.CaptureDiagnostics().CommittedUnits, Is.EqualTo(5_500));
        });
    }

    [Test]
    public void InsertMessageAsync_On_Transport_Exception_Commits_Quota_Lease()
    {
        using var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("network timeout"));
        var client = CreateClient(handler);

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() =>
            client.InsertMessageAsync("live-chat-1", "привет", YouTubeQuotaPriority.CommandReply, CancellationToken.None));

        var diagnostics = _quotaGovernor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(diagnostics.CommittedUnits, Is.EqualTo(20));
            Assert.That(diagnostics.ReservedUnits, Is.Zero);
        });
    }

    private YouTubeRestLiveChatSendClient CreateClient(StubHttpHandler innerHandler)
    {
        var authService = new TestAuthService(_accountsStore);
        var authHandler = new BotYouTubeAuthHandler(authService, NullLogger<BotYouTubeAuthHandler>.Instance)
        {
            InnerHandler = innerHandler,
        };
        var httpClient = new HttpClient(authHandler, disposeHandler: true)
        {
            BaseAddress = YouTubeEndpoints.RestApiBaseUri,
        };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(YouTubeEndpoints.BotApiHttpClient).Returns(httpClient);

        return new YouTubeRestLiveChatSendClient(factory, _quotaGovernor);
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class TestAuthService(YouTubeAccountsStore accountsStore) : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => !string.IsNullOrWhiteSpace(accountsStore.Load(role).AccessToken);

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(accountsStore.Load(role).AccessToken);

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken)
            => accountsStore.TryClearAccessToken(role, failedAccessToken);

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }
}
