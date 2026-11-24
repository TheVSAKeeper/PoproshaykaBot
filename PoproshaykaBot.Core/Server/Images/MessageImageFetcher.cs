using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Obs;
using System.Net;

namespace PoproshaykaBot.Core.Server.Images;

public sealed record MessageImageContent(byte[] Content, string MediaType);

public sealed class MessageImageFetcher(
    IHttpClientFactory httpClientFactory,
    ILogger<MessageImageFetcher>? logger = null)
{
    public const string HttpClientName = "message-image-proxy";
    public const int MaxContentBytes = 5 * 1024 * 1024;
    public const int MaxRedirects = 3;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public async Task<MessageImageContent?> FetchAsync(
        Uri uri,
        ObsChatSettings settings,
        CancellationToken cancellationToken,
        ILogger? logTarget = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(settings);

        var activeLogger = logTarget ?? logger;

        if (!settings.ShowMessageImages)
        {
            Refuse(activeLogger, "картинки в сообщениях выключены");
            return null;
        }

        if (!MessageImageUrlValidator.IsAllowed(uri.AbsoluteUri, settings.MessageImageAllowedHosts, out var validated))
        {
            Refuse(activeLogger, "ссылка не прошла проверку (схема, хост или расширение)");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var current = validated;

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (!MessageImageUrlValidator.TryParseRedirect(current, response.Headers.Location, out var next)
                        || !MessageImageHosts.IsAllowed(next.Host, settings.MessageImageAllowedHosts))
                    {
                        Refuse(activeLogger, "редирект уводит на другой хост");
                        return null;
                    }

                    current = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Refuse(activeLogger, $"источник ответил {(int)response.StatusCode}");
                    return null;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;

                if (!MessageImageUrlValidator.IsAllowedMediaType(mediaType))
                {
                    Refuse(activeLogger, $"тип ответа {mediaType ?? "неизвестен"}, а не картинка");
                    return null;
                }

                if (response.Content.Headers.ContentLength > MaxContentBytes)
                {
                    Refuse(activeLogger, $"тело больше {MaxContentBytes} байт");
                    return null;
                }

                var content = await ReadCappedAsync(response.Content, timeout.Token);

                if (content == null)
                {
                    Refuse(activeLogger, $"тело больше {MaxContentBytes} байт");
                    return null;
                }

                return new MessageImageContent(content, mediaType!);
            }

            Refuse(activeLogger, $"больше {MaxRedirects} редиректов подряд");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Refuse(activeLogger, $"превышен таймаут {RequestTimeout.TotalSeconds} с");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            Refuse(activeLogger, $"запрос не удался: {exception.Message}");
            return null;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxContentBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static void Refuse(ILogger? logger, string reason)
    {
        logger?.LogDebug("Загрузка картинки {Path}: картинка не отдана – {Reason}", MessageImagePolicy.ProxyPath, reason);
    }
}
