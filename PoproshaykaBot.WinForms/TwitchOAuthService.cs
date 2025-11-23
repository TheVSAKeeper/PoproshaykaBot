using PoproshaykaBot.WinForms.Settings;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.WinForms;

public class TwitchOAuthService(SettingsManager settingsManager)
{
    private TaskCompletionSource<string>? _authTcs;

    public event Action<string>? StatusChanged;

    public async Task<string> StartOAuthFlowAsync(string clientId, string clientSecret, string[]? scopes = null, string? redirectUri = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ID клиента не может быть пустым", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Секрет клиента не может быть пустым", nameof(clientSecret));
        }

        var settings = settingsManager.Current.Twitch;
        scopes ??= settings.Scopes;
        redirectUri ??= settings.RedirectUri;

        var scopeString = string.Join(" ", scopes);

        StatusChanged?.Invoke("Открытие браузера для авторизации...");

        var authUrl = $"https://id.twitch.tv/oauth2/authorize"
                      + $"?response_type=code"
                      + $"&client_id={clientId}"
                      + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                      + $"&scope={Uri.EscapeDataString(scopeString)}";

        _authTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Не удалось открыть браузер: {exception.Message}", exception);
        }

        StatusChanged?.Invoke("Ожидание авторизации пользователя...");

        var authorizationCode = await _authTcs.Task;

        if (string.IsNullOrEmpty(authorizationCode))
        {
            throw new InvalidOperationException("Не удалось получить код авторизации");
        }

        StatusChanged?.Invoke("Обмен кода на токен доступа...");
        var accessToken = await ExchangeCodeForTokenAsync(clientId, clientSecret, authorizationCode, redirectUri);

        StatusChanged?.Invoke("Авторизация завершена успешно!");
        return accessToken;
    }

    public void SetAuthResult(string code)
    {
        _authTcs?.TrySetResult(code);
    }

    public void SetAuthError(Exception exception)
    {
        _authTcs?.TrySetException(exception);
    }

    public async Task<bool> IsTokenValidAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            using var client = new HttpClient();
            var validateUrl = "https://id.twitch.tv/oauth2/validate";
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var response = await client.GetAsync(validateUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TokenResponse> RefreshTokenAsync(string clientId, string clientSecret, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token не может быть пустым", nameof(refreshToken));
        }

        StatusChanged?.Invoke("Обновление токена доступа...");

        using var client = new HttpClient();
        var tokenUrl = "https://id.twitch.tv/oauth2/token";

        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" },
        };

        using var content = new FormUrlEncodedContent(formData);
        var response = await client.PostAsync(tokenUrl, content);
        var jsonResponse = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ошибка обновления токена: {jsonResponse}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Не удалось десериализовать ответ сервера");
        }

        StatusChanged?.Invoke("Токен доступа обновлен успешно!");
        return tokenResponse;
    }

    public async Task<string> GetValidTokenAsync(string clientId, string clientSecret, string currentToken, string refreshToken)
    {
        StatusChanged?.Invoke("Проверка действительности токена...");

        if (await IsTokenValidAsync(currentToken))
        {
            StatusChanged?.Invoke("Токен действителен");
            return currentToken;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Токен недействителен и refresh token отсутствует. Требуется повторная авторизация.");
        }

        var tokenResponse = await RefreshTokenAsync(clientId, clientSecret, refreshToken);
        return tokenResponse.AccessToken;
    }

    private async Task<string> ExchangeCodeForTokenAsync(string clientId, string clientSecret, string authorizationCode, string redirectUri)
    {
        using var client = new HttpClient();
        var tokenUrl = "https://id.twitch.tv/oauth2/token";

        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "code", authorizationCode },
            { "grant_type", "authorization_code" },
            { "redirect_uri", redirectUri },
        };

        using var content = new FormUrlEncodedContent(formData);
        var response = await client.PostAsync(tokenUrl, content);
        var jsonResponse = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ошибка получения токена: {jsonResponse}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Не удалось десериализовать ответ сервера");
        }

        var settings = settingsManager.Current;
        settings.Twitch.AccessToken = tokenResponse.AccessToken;
        settings.Twitch.RefreshToken = tokenResponse.RefreshToken;
        settingsManager.SaveSettings(settings);

        return tokenResponse.AccessToken;
    }
}

public record TokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn,
    [property: JsonPropertyName("refresh_token")]
    string RefreshToken,
    [property: JsonPropertyName("scope")]
    List<string> Scope,
    [property: JsonPropertyName("token_type")]
    string TokenType
);
