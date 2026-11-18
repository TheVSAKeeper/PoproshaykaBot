using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Helix;

public abstract class TwitchAuthHandlerBase(
    ITwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger logger,
    TwitchChannelAuthorization? channelAuthorization = null)
    : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> KeepTokenOnUnauthorized = new("poproshayka.keep-token-on-401");

    protected abstract PlatformAuthRole Role { get; }

    protected abstract string MissingTokenHint { get; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var contextId = PlatformAuthRequestOptions.ResolveContextId(request);
        if (channelAuthorization is not null
            && PlatformAuthRequestOptions.CurrentContextId is null
            && !request.Options.TryGetValue(PlatformAuthRequestOptions.ContextIdKey, out _)
            && !channelAuthorization.TryGetAccount(Role, out contextId, out _))
        {
            throw new TwitchAuthorizationMissingException($"Подтвердите доступ аккаунта к каналу Twitch. {MissingTokenHint}");
        }

        var token = await GetTokenAsync(oauthService, contextId, cancellationToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TwitchAuthorizationMissingException($"Токен Twitch для роли {Role} (контекст {contextId}) отсутствует или недействителен. {MissingTokenHint}");
        }

        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("Client-Id", settingsManager.Current.Twitch.ClientId);

        var response = await base.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode != 401)
        {
            return response;
        }

        if (request.Options.TryGetValue(KeepTokenOnUnauthorized, out var keepToken) && keepToken)
        {
            logger.LogDebug("Helix запрос {Method} {Path} (роль {Role}, контекст {ContextId}) вернул 401 – отказ относится к самой операции, токен не очищается",
                request.Method,
                request.RequestUri?.AbsolutePath,
                Role,
                contextId);

            return response;
        }

        logger.LogWarning("Helix запрос {Method} {Path} (роль {Role}, контекст {ContextId}) вернул 401 – пробуем очистить сохранённый токен",
            request.Method,
            request.RequestUri?.AbsolutePath,
            Role,
            contextId);

        var cleared = accountsStore.TryClearAccessToken(Role, contextId, token);

        if (!cleared)
        {
            logger.LogDebug("Helix 401 (роль {Role}, контекст {ContextId}): токен в хранилище уже сменился, оставляем без изменений",
                Role,
                contextId);
        }

        return response;
    }

    protected virtual Task<string?> GetTokenAsync(
        ITwitchOAuthService oauthService,
        string contextId,
        CancellationToken cancellationToken)
    {
        return AuthContextIds.IsDefault(contextId)
            ? GetTokenAsync(oauthService, cancellationToken)
            : oauthService.GetAccessTokenAsync(Role, contextId, cancellationToken);
    }

    protected abstract Task<string?> GetTokenAsync(ITwitchOAuthService oauthService, CancellationToken cancellationToken);
}
