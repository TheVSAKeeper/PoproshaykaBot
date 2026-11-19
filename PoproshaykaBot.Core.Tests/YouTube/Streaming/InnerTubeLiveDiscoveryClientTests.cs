using System.Net;
using System.Text;
using NSubstitute;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Streaming;

[TestFixture]
public class InnerTubeLiveDiscoveryClientTests
{
    private const string ChannelId = "UC0000000000000000000001";

    [Test]
    public async Task Ok_Live_Html_Returns_Video_And_Hits_Channel_Live_Path()
    {
        Uri? capturedUri = null;
        var client = CreateClient(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <script>var ytInitialPlayerResponse = {
                      "playabilityStatus": { "status": "OK" },
                      "videoDetails": {
                        "videoId": "vid-200",
                        "channelId": "UC0000000000000000000001",
                        "author": "ChannelName",
                        "title": "Live Now",
                        "isLive": true
                      }
                    };</script>
                    """, Encoding.UTF8, "text/html"),
            };
        });

        var result = await client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(capturedUri?.AbsolutePath, Is.EqualTo($"/channel/{ChannelId}/live"));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.VideoId, Is.EqualTo("vid-200"));
            Assert.That(result.ChannelTitle, Is.EqualTo("ChannelName"));
            Assert.That(result.Title, Is.EqualTo("Live Now"));
        });
    }

    [Test]
    public async Task NotFound_Returns_Null()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void Non_Success_Status_Throws_Transient_Never_Unauthorized_Or_QuotaExceeded(HttpStatusCode statusCode)
    {
        var client = CreateClient(_ => new HttpResponseMessage(statusCode));

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() => client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
    }

    [Test]
    public void Network_Error_Throws_Transient()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection reset"));

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() => client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
    }

    [TestCase("invalid-charset-xyz")]
    [TestCase("utf-7")]
    public async Task Unsupported_Or_Invalid_Charset_Header_Falls_Back_To_Utf8(string charSet)
    {
        var client = CreateClient(_ =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("""
                <script>var ytInitialPlayerResponse = {
                  "playabilityStatus": { "status": "OK" },
                  "videoDetails": {
                    "videoId": "vid-charset",
                    "channelId": "UC0000000000000000000001",
                    "author": "Channel",
                    "title": "Привет",
                    "isLive": true
                  }
                };</script>
                """));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html")
            {
                CharSet = charSet,
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var result = await client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.VideoId, Is.EqualTo("vid-charset"));
            Assert.That(result.Title, Is.EqualTo("Привет"));
        });
    }

    [Test]
    public void Oversized_Response_Body_Throws_Transient()
    {
        var oversizedBytes = new byte[(8 * 1024 * 1024) + 1024];
        Array.Fill(oversizedBytes, (byte)'a');
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(oversizedBytes),
        });

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() => client.FindActiveLiveVideoAsync(ChannelId, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
    }

    private static InnerTubeLiveDiscoveryClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(InnerTubeEndpoints.HttpClientName)
            .Returns(_ => new HttpClient(new DelegateHandler(responder))
            {
                BaseAddress = InnerTubeEndpoints.BaseUri,
            });

        return new InnerTubeLiveDiscoveryClient(factory);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
