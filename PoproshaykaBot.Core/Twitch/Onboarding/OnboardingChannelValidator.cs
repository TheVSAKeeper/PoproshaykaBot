using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PoproshaykaBot.Core.Twitch.Onboarding;

public sealed class OnboardingChannelValidator(
    IHttpClientFactory httpClientFactory,
    ILogger<OnboardingChannelValidator> logger)
    : IOnboardingChannelValidator
{
    private static readonly TimeSpan AppTokenSafetyMargin = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _appTokenLock = new(1, 1);
    private CachedAppToken? _cachedAppToken;

    public async Task<ChannelValidationResult> ValidateAsync(
        string login,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(accessToken))
        {
            return ChannelValidationResult.Skipped;
        }

        try
        {
            using var http = httpClientFactory.CreateClient();
            return await ProbeUserExistsAsync(http, login, clientId, accessToken, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ChannelValidationResult.Skipped;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось проверить канал {Login} через Helix", login);
            return ChannelValidationResult.Skipped;
        }
    }

    public async Task<ChannelValidationResult> ValidateWithAppTokenAsync(
        string login,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return ChannelValidationResult.Skipped;
        }

        try
        {
            using var http = httpClientFactory.CreateClient();

            var appToken = await RequestAppTokenAsync(http, clientId, clientSecret, cancellationToken);

            if (string.IsNullOrEmpty(appToken))
            {
                return ChannelValidationResult.Skipped;
            }

            return await ProbeUserExistsAsync(http, login, clientId, appToken, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ChannelValidationResult.Skipped;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось проверить канал {Login} через app-токен", login);
            return ChannelValidationResult.Skipped;
        }
    }

    private async Task<ChannelValidationResult> ProbeUserExistsAsync(
        HttpClient http,
        string login,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{TwitchEndpoints.HelixBaseUrl}{TwitchEndpoints.HelixUsers}?login={Uri.EscapeDataString(login)}");

        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Helix users-проверка вернула {StatusCode} для {Login}", response.StatusCode, login);
            return ChannelValidationResult.Skipped;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var hasUser = doc.RootElement.TryGetProperty("data", out var data)
                      && data.ValueKind == JsonValueKind.Array
                      && data.GetArrayLength() > 0;

        return hasUser ? ChannelValidationResult.Found : ChannelValidationResult.NotFound;
    }

    private async Task<string?> RequestAppTokenAsync(
        HttpClient http,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var cached = _cachedAppToken;
        if (cached is not null
            && string.Equals(cached.ClientId, clientId, StringComparison.Ordinal)
            && cached.ExpiresAt - AppTokenSafetyMargin > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }

        await _appTokenLock.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedAppToken;
            if (cached is not null
                && string.Equals(cached.ClientId, clientId, StringComparison.Ordinal)
                && cached.ExpiresAt - AppTokenSafetyMargin > DateTimeOffset.UtcNow)
            {
                return cached.Token;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials",
            });

            using var response = await http.PostAsync(TwitchEndpoints.OAuthToken, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Twitch вернул {StatusCode} при запросе app-токена для проверки канала", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var expiresInSeconds = 3600;
            if (doc.RootElement.TryGetProperty("expires_in", out var expiresElement)
                && expiresElement.ValueKind == JsonValueKind.Number
                && expiresElement.TryGetInt32(out var parsedExpires))
            {
                expiresInSeconds = parsedExpires;
            }

            _cachedAppToken = new(clientId, token, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds));
            return token;
        }
        finally
        {
            _appTokenLock.Release();
        }
    }

    private sealed record CachedAppToken(string ClientId, string Token, DateTimeOffset ExpiresAt);
}
