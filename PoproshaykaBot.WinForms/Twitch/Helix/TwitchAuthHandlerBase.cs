using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public abstract class TwitchAuthHandlerBase(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
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

        logger.LogWarning("Helix запрос {Method} {Path} (роль {Role}) вернул 401 — очищаем сохранённый токен",
            request.Method, request.RequestUri?.AbsolutePath, Role);

        var settings = settingsManager.Current;
        var account = GetAccount(settings.Twitch);

        if (!string.Equals(account.AccessToken, token, StringComparison.Ordinal))
        {
            return response;
        }

        account.AccessToken = string.Empty;
        settingsManager.SaveSettings(settings);

        return response;
    }

    protected abstract Task<string?> GetTokenAsync(TwitchOAuthService oauthService, CancellationToken cancellationToken);

    protected abstract TwitchAccountSettings GetAccount(TwitchSettings twitch);
}
