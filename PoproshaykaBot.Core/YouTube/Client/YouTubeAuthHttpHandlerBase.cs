using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using System.Net;
using System.Net.Http.Headers;

namespace PoproshaykaBot.Core.YouTube.Client;

internal abstract class YouTubeAuthHttpHandlerBase(
    IPlatformAuthService authService,
    ILogger logger,
    YouTubeAccountsStore? accountsStore = null)
    : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> KeepTokenOnUnauthorized = new("poproshayka.youtube.keep-token-on-401");

    protected abstract PlatformAuthRole Role { get; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var contextId = PlatformAuthRequestOptions.ResolveContextId(request);
        var token = await authService.GetAccessTokenAsync(Role, contextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Unauthorized,
                $"Токен YouTube для роли {Role} (контекст {contextId}) отсутствует или недействителен.",
                statusCode: HttpStatusCode.Unauthorized);
        }

        if (PlatformAuthRequestOptions.ExpectedAccountId is { } expected
            && (accountsStore?.Load(Role, contextId) is not { } current
                || !string.Equals(current.ChannelId, expected, StringComparison.Ordinal)
                || !string.Equals(current.AccessToken, token, StringComparison.Ordinal)))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Unauthorized, "Выбранный аккаунт YouTube изменился до отправки.",
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
                "YouTube API запрос {Method} {Path} (роль {Role}, контекст {ContextId}) вернул 401 – отказ относится к самой операции, токен не очищается",
                request.Method,
                request.RequestUri?.AbsolutePath,
                Role,
                contextId);

            return response;
        }

        logger.LogWarning(
            "YouTube API запрос {Method} {Path} (роль {Role}, контекст {ContextId}) вернул 401 – очищаем сохранённый токен",
            request.Method,
            request.RequestUri?.AbsolutePath,
            Role,
            contextId);

        if (!authService.TryClearAccessToken(Role, contextId, token))
        {
            logger.LogDebug(
                "YouTube API 401 (роль {Role}, контекст {ContextId}): токен в хранилище уже сменился, оставляем без изменений",
                Role,
                contextId);
        }

        return response;
    }
}
