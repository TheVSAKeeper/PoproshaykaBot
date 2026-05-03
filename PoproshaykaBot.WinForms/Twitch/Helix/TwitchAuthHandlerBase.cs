using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public abstract class TwitchAuthHandlerBase(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger logger)
    : DelegatingHandler
{
    protected abstract TwitchOAuthRole Role { get; }

    protected abstract string MissingTokenHint { get; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(oauthService, cancellationToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TwitchAuthorizationMissingException($"Токен Twitch для роли {Role} отсутствует или недействителен. {MissingTokenHint}");
        }

        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("Client-Id", settingsManager.Current.Twitch.ClientId);

        var response = await base.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode != 401)
        {
            return response;
        }

        logger.LogWarning("Helix запрос {Method} {Path} (роль {Role}) вернул 401 — пробуем очистить сохранённый токен",
            request.Method,
            request.RequestUri?.AbsolutePath,
            Role);

        var cleared = accountsStore.TryClearAccessToken(Role, token);

        if (!cleared)
        {
            logger.LogDebug("Helix 401 (роль {Role}): токен в хранилище уже сменился, оставляем без изменений",
                Role);
        }

        return response;
    }

    protected abstract Task<string?> GetTokenAsync(TwitchOAuthService oauthService, CancellationToken cancellationToken);
}
