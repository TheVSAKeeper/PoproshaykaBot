using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using System.Net;

namespace PoproshaykaBot.Core.Tests.YouTube.Client;

[TestFixture]
public class YouTubeAuthHttpHandlerTests
{
    [Test]
    public async Task Unauthorized_Response_Clears_Access_Token_Without_Inline_Refresh()
    {
        var auth = new StubPlatformAuthService("bot-token-1");

        var inner = new StubMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var handler = new BotYouTubeAuthHandler(auth, NullLogger<BotYouTubeAuthHandler>.Instance)
        {
            InnerHandler = inner,
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://www.googleapis.com/youtube/v3/test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(auth.ClearedCalls, Is.EqualTo(new[] { (PlatformAuthRole.Bot, "bot-token-1") }));
            Assert.That(auth.ForceRefreshCalls, Is.Zero);
        });
    }

    [Test]
    public async Task KeepTokenOnUnauthorized_Skips_ClearAccessToken()
    {
        var auth = new StubPlatformAuthService("broadcaster-token");

        var inner = new StubMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var handler = new BroadcasterYouTubeAuthHandler(auth, NullLogger<BroadcasterYouTubeAuthHandler>.Instance)
        {
            InnerHandler = inner,
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/test");
        request.Options.Set(YouTubeAuthHttpHandlerBase.KeepTokenOnUnauthorized, true);

        using var response = await client.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(auth.ClearedCalls, Is.Empty);
        });
    }

    private sealed class StubPlatformAuthService(string token) : IPlatformAuthService
    {
        public List<(PlatformAuthRole Role, string Token)> ClearedCalls { get; } = [];

        public int ForceRefreshCalls { get; private set; }

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public ValueTask<string?> GetAccessTokenAsync(
            PlatformAuthRole role,
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            if (forceRefresh)
            {
                ForceRefreshCalls++;
            }

            return ValueTask.FromResult<string?>(token);
        }

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken)
        {
            ClearedCalls.Add((role, failedAccessToken));
            return true;
        }

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
