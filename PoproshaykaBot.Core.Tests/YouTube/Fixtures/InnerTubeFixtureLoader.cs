using System.Net;
using System.Reflection;
using System.Text;

namespace PoproshaykaBot.Core.Tests.YouTube.Fixtures;

internal static class InnerTubeFixtureLoader
{
    private const string Prefix = "PoproshaykaBot.Core.Tests.YouTube.Fixtures.InnerTube.";

    private static readonly Assembly TestAssembly = typeof(InnerTubeFixtureLoader).Assembly;

    public static IReadOnlyList<string> GetAllFixtureResourceNames() =>
        TestAssembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string ReadText(string fileName)
    {
        using var stream = TestAssembly.GetManifestResourceStream(Prefix + fileName)
            ?? throw new InvalidOperationException($"Не найдена встроенная фикстура '{fileName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

internal sealed class QueueHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    private int _callIndex;

    public List<Uri?> RequestedUris { get; } = [];

    public List<string?> RequestBodies { get; } = [];

    public List<IReadOnlyDictionary<string, IEnumerable<string>>> RequestHeaders { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _callIndex) - 1;
        RequestedUris.Add(request.RequestUri);
        RequestHeaders.Add(request.Headers.ToDictionary(
            static h => h.Key,
            static h => h.Value,
            StringComparer.OrdinalIgnoreCase));

        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : null;
        RequestBodies.Add(body);

        return responder(request, index);
    }

    public static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string content, string mediaType) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType),
        };
}

internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null)
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress ?? new Uri("https://www.youtube.com", UriKind.Absolute),
        };
}
