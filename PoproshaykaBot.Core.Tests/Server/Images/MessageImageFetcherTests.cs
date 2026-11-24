using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings.Obs;
using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Tests.Server.Images;

[TestFixture]
public sealed class MessageImageFetcherTests
{
    private static readonly Uri SourceUri = new("https://i.imgur.com/abc.png");

    private RecordingLogger<MessageImageFetcher> _logger = null!;
    private StubUpstreamHandler _upstream = null!;
    private StubHttpClientFactory _httpClientFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new();
        _upstream = new();
        _httpClientFactory = new(_upstream);
    }

    [TearDown]
    public void TearDown()
    {
        _upstream.Dispose();
    }

    [Test]
    public async Task FetchAsync_Disabled_ReturnsNullWithoutNetwork()
    {
        _upstream.Respond(Png);
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, new ObsChatSettings { ShowMessageImages = false }, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_upstream.Requests, Is.Empty);
        AssertRefusedQuietly();
    }

    [Test]
    public async Task FetchAsync_DisallowedHost_ReturnsNullWithoutNetwork()
    {
        _upstream.Respond(Png);
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(
            new Uri("https://evil.example/abc.png"),
            EnabledSettings(),
            CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_upstream.Requests, Is.Empty);
        AssertRefusedQuietly();
    }

    [Test]
    public async Task FetchAsync_AllowedSource_ReturnsBytesAndMediaType()
    {
        _upstream.Respond(Png);
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, EnabledSettings(), CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.MediaType, Is.EqualTo("image/png"));
            Assert.That(result.Content, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        }
    }

    [Test]
    public async Task FetchAsync_NonImageContentType_ReturnsNull()
    {
        _upstream.Respond(() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("<html></html>", Encoding.UTF8, "text/html"),
        });
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, EnabledSettings(), CancellationToken.None);

        Assert.That(result, Is.Null);
        AssertRefusedQuietly();
    }

    [Test]
    public async Task FetchAsync_BodyExceedingCap_ReturnsNull()
    {
        _upstream.Respond(() =>
        {
            var content = new ByteArrayContent(new byte[MessageImageFetcher.MaxContentBytes + 1]);
            content.Headers.ContentType = new("image/png");
            return new(HttpStatusCode.OK) { Content = content };
        });
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, EnabledSettings(), CancellationToken.None);

        Assert.That(result, Is.Null);
        AssertRefusedQuietly();
    }

    [Test]
    public async Task FetchAsync_TooManyRedirects_ReturnsNull()
    {
        var hop = 0;
        _upstream.Respond(() =>
        {
            hop++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new($"https://i.imgur.com/r{hop}.png");
            return redirect;
        });
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, EnabledSettings(), CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_upstream.Requests, Has.Count.EqualTo(MessageImageFetcher.MaxRedirects + 1));
        AssertRefusedQuietly();
    }

    [Test]
    public async Task FetchAsync_RedirectToAnotherHost_IsRefusedImmediately()
    {
        _upstream.Respond(() =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new("https://evil.example/abc.png");
            return redirect;
        });
        var fetcher = new MessageImageFetcher(_httpClientFactory, _logger);

        var result = await fetcher.FetchAsync(SourceUri, EnabledSettings(), CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_upstream.Requests, Has.Count.EqualTo(1));
        AssertRefusedQuietly();
    }

    private static ObsChatSettings EnabledSettings() => new() { ShowMessageImages = true };

    private static HttpResponseMessage Png()
    {
        var content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        content.Headers.ContentType = new("image/png");
        return new(HttpStatusCode.OK) { Content = content };
    }

    private void AssertRefusedQuietly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_logger.Entries.Any(entry => entry.Level == LogLevel.Debug), Is.True);
            Assert.That(_logger.Entries.Any(entry => entry.Level >= LogLevel.Warning), Is.False);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.That(name, Is.EqualTo(MessageImageFetcher.HttpClientName));
            return new(handler, disposeHandler: false);
        }
    }

    private sealed class StubUpstreamHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _factory = () => new(HttpStatusCode.NotFound);

        public List<string> Requests { get; } = [];

        public void Respond(Func<HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            return Task.FromResult(_factory());
        }
    }
}
