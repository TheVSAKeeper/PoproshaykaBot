using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal sealed class InnerTubeLiveChatClient(IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public async Task<InnerTubeBootstrapResult> FetchWatchBootstrapAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        using var client = CreateConfiguredClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, InnerTubeEndpoints.BuildWatchPath(videoId));
        using var timeoutCts = CreateRequestTimeoutCts(cancellationToken);

        using var response = await SendRequestAsync(
            client,
            request,
            HttpCompletionOption.ResponseContentRead,
            "загрузке страницы трансляции InnerTube",
            cancellationToken,
            timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response.StatusCode, "загрузка страницы трансляции InnerTube");
        }

        string html;

        try
        {
            html = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsMappableBodyReadException(ex, cancellationToken))
        {
            throw CreateBodyReadException(ex, "страницы трансляции InnerTube", response.StatusCode, cancellationToken);
        }

        try
        {
            return InnerTubeLiveChatParser.ParseWatchBootstrap(html, videoId);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "Превышен таймаут регулярного выражения при разборе страницы трансляции InnerTube.",
                response.StatusCode,
                ex);
        }
    }

    public async Task<LiveChatResponse> FetchChatPageAsync(
        string apiKey,
        LiveChatRequest requestPayload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(requestPayload);

        using var client = CreateConfiguredClient();
        var serializedRequest = JsonSerializer.Serialize(requestPayload, InnerTubeJsonSerializerContext.Default.LiveChatRequest);
        using var content = new StringContent(serializedRequest, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, InnerTubeEndpoints.BuildGetLiveChatPath(apiKey))
        {
            Content = content,
        };
        using var timeoutCts = CreateRequestTimeoutCts(cancellationToken);

        using var response = await SendRequestAsync(
            client,
            request,
            HttpCompletionOption.ResponseHeadersRead,
            "запросе InnerTube get_live_chat",
            cancellationToken,
            timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response.StatusCode, "запрос InnerTube get_live_chat");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                InnerTubeJsonSerializerContext.Default.LiveChatResponse,
                timeoutCts.Token).ConfigureAwait(false);

            return parsed ?? throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "Ответ InnerTube get_live_chat пустой после десериализации.",
                response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "Ошибка разбора JSON ответа InnerTube get_live_chat.",
                response.StatusCode,
                ex);
        }
        catch (Exception ex) when (IsMappableBodyReadException(ex, cancellationToken))
        {
            throw CreateBodyReadException(ex, "ответа InnerTube get_live_chat", response.StatusCode, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        string operationName,
        CancellationToken callerCt,
        CancellationToken requestCt)
    {
        try
        {
            return await client.SendAsync(request, completionOption, requestCt).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Сетевая ошибка при {operationName}.",
                innerException: ex);
        }
        catch (IOException ex)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Ошибка ввода-вывода при {operationName}.",
                innerException: ex);
        }
        catch (OperationCanceledException ex) when (!callerCt.IsCancellationRequested)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Таймаут при {operationName}.",
                innerException: ex);
        }
    }

    private static bool IsMappableBodyReadException(Exception ex, CancellationToken callerCt) =>
        ex is HttpRequestException or IOException or InvalidDataException
        || (ex is OperationCanceledException && !callerCt.IsCancellationRequested);

    private static InnerTubeChatException CreateBodyReadException(
        Exception ex,
        string targetName,
        HttpStatusCode statusCode,
        CancellationToken callerCt) =>
        ex switch
        {
            HttpRequestException => new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Ошибка чтения {targetName}.",
                statusCode,
                ex),
            IOException or InvalidDataException => new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Поток {targetName} оборван или повреждён при чтении.",
                statusCode,
                ex),
            OperationCanceledException when !callerCt.IsCancellationRequested => new InnerTubeChatException(
                InnerTubeErrorKind.TransientNetwork,
                $"Таймаут чтения {targetName}.",
                statusCode,
                ex),
            _ => throw new InvalidOperationException($"Неподдерживаемый тип исключения чтения: {ex.GetType().Name}.", ex),
        };

    private static CancellationTokenSource CreateRequestTimeoutCts(CancellationToken callerCt)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        timeoutCts.CancelAfter(RequestTimeout);
        return timeoutCts;
    }

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

    private static InnerTubeChatException CreateHttpException(HttpStatusCode statusCode, string operation)
    {
        var kind = statusCode switch
        {
            HttpStatusCode.TooManyRequests => InnerTubeErrorKind.RateLimited,
            HttpStatusCode.Forbidden => InnerTubeErrorKind.Forbidden,
            HttpStatusCode.Unauthorized => InnerTubeErrorKind.LoginRequired,
            HttpStatusCode.NotFound => InnerTubeErrorKind.StreamUnavailable,
            >= HttpStatusCode.InternalServerError => InnerTubeErrorKind.TransientNetwork,
            _ => InnerTubeErrorKind.Unknown,
        };

        return new(
            kind,
            $"InnerTube ({operation}) вернул HTTP {(int)statusCode} ({statusCode}).",
            statusCode);
    }
}
