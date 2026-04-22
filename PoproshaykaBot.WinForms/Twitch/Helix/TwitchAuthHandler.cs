using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class TwitchAuthHandler(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
    ILogger<TwitchAuthHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await oauthService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Не удалось получить токен Twitch для Helix-запроса");
        }

        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("Client-Id", settingsManager.Current.Twitch.ClientId);

        var response = await base.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode != 401)
        {
            return response;
        }

        logger.LogWarning("Helix запрос {Method} {Path} вернул 401 — очищаем сохранённый токен",
            request.Method, request.RequestUri?.AbsolutePath);

        var settings = settingsManager.Current;
        if (!string.Equals(settings.Twitch.AccessToken, token, StringComparison.Ordinal))
        {
            return response;
        }

        settings.Twitch.AccessToken = string.Empty;
        settingsManager.SaveSettings(settings);

        return response;
    }
}
