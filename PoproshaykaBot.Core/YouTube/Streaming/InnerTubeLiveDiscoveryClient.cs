using System.Net;
using System.Text;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class InnerTubeLiveDiscoveryClient(IHttpClientFactory httpClientFactory) : IYouTubePublicLiveDiscoveryClient
{
    private const string OperationName = "innertube.channel.live";
    private const int MaxHtmlBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public async Task<YouTubePublicLiveVideo?> FindActiveLiveVideoAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        using var client = CreateConfiguredClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, InnerTubeEndpoints.BuildChannelLivePath(channelId));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsMappableTransportException(ex, cancellationToken))
        {
            throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new YouTubeApiException(
                    YouTubeApiErrorKind.Transient,
                    $"Страница канала YouTube ({OperationName}) вернула HTTP {(int)response.StatusCode}.",
                    statusCode: response.StatusCode);
            }

            string html;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                var totalRead = 0;

                while (true)
                {
                    var read = await stream.ReadAsync(chunk, timeoutCts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                    if (totalRead > MaxHtmlBytes)
                    {
                        throw new YouTubeApiException(
                            YouTubeApiErrorKind.Transient,
                            $"Ответ страницы канала YouTube ({OperationName}) превысил допустимый размер.");
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, read), timeoutCts.Token).ConfigureAwait(false);
                }

                var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
                html = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
            catch (YouTubeApiException)
            {
                throw;
            }
            catch (Exception ex) when (IsMappableTransportException(ex, cancellationToken))
            {
                throw YouTubeErrorClassifier.FromTransportException(ex, OperationName);
            }

            return InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, channelId);
        }
    }

    private static Encoding ResolveEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet))
        {
            return Encoding.UTF8;
        }

        var trimmed = charSet.Trim().Trim('"');
        try
        {
            return Encoding.GetEncoding(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    private static bool IsMappableTransportException(Exception ex, CancellationToken callerCt) =>
        ex is HttpRequestException or IOException or InvalidDataException
        || (ex is OperationCanceledException && !callerCt.IsCancellationRequested);

    private HttpClient CreateConfiguredClient()
    {
        var client = httpClientFactory.CreateClient(InnerTubeEndpoints.HttpClientName);
        client.BaseAddress ??= InnerTubeEndpoints.BaseUri;

        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(InnerTubeEndpoints.UserAgent);
        }

        if (client.DefaultRequestHeaders.AcceptLanguage.Count == 0)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(InnerTubeEndpoints.AcceptLanguage);
        }

        return client;
    }
}
