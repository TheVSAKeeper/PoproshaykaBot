using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using System.Net;
using System.Net.Http.Headers;

namespace PoproshaykaBot.Core.YouTube.Client;

internal abstract class YouTubeAuthHttpHandlerBase(
    IPlatformAuthService authService,
    ILogger logger)
    : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> KeepTokenOnUnauthorized = new("poproshayka.youtube.keep-token-on-401");

    protected abstract PlatformAuthRole Role { get; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await authService.GetAccessTokenAsync(Role, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Unauthorized,
                $"Токен YouTube для роли {Role} отсутствует или недействителен.",
                statusCode: HttpStatusCode.Unauthorized);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        if (request.Options.TryGetValue(KeepTokenOnUnauthorized, out var keepToken) && keepToken)
        {
            logger.LogDebug(
                "YouTube API запрос {Method} {Path} (роль {Role}) вернул 401 – отказ относится к самой операции, токен не очищается",
                request.Method,
                request.RequestUri?.AbsolutePath,
                Role);

            return response;
        }

        logger.LogWarning(
            "YouTube API запрос {Method} {Path} (роль {Role}) вернул 401 – очищаем сохранённый токен",
            request.Method,
            request.RequestUri?.AbsolutePath,
            Role);

        if (!authService.TryClearAccessToken(Role, token))
        {
            logger.LogDebug(
                "YouTube API 401 (роль {Role}): токен в хранилище уже сменился, оставляем без изменений",
                Role);
        }

        return response;
    }
}
