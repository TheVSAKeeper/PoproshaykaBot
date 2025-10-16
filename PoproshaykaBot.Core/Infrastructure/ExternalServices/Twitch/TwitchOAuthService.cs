﻿using PoproshaykaBot.Core.Domain.Models.Connection;
using PoproshaykaBot.Core.Infrastructure.Http;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Infrastructure.ExternalServices.Twitch;

public class TwitchOAuthService(SettingsManager settingsManager)
{
    public event Action<string>? StatusChanged;

    public async Task<string> StartOAuthFlowAsync(string clientId, string clientSecret, UnifiedHttpServer? httpServer = null, string[]? scopes = null, string? redirectUri = null)
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

        Task<string> codeTask;

        if (httpServer != null)
        {
            codeTask = httpServer.WaitForOAuthCodeAsync();
        }
        else
        {
            // TODO: Для обратной совместимости
            codeTask = StartLocalHttpServerAsync(redirectUri);
        }

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
        var authorizationCode = await codeTask;

        if (string.IsNullOrEmpty(authorizationCode))
        {
            throw new InvalidOperationException("Не удалось получить код авторизации");
        }

        StatusChanged?.Invoke("Обмен кода на токен доступа...");
        var accessToken = await ExchangeCodeForTokenAsync(clientId, clientSecret, authorizationCode, redirectUri);

        StatusChanged?.Invoke("Авторизация завершена успешно!");
        return accessToken;
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

    private async Task<string> StartLocalHttpServerAsync(string redirectUri)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirectUri}/");

        try
        {
            listener.Start();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Не удалось запустить локальный сервер на {redirectUri}: {exception.Message}", exception);
        }

        try
        {
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"Ошибка авторизации: {error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("Не удалось получить код авторизации");
            }

            var responseString =
                """
                <!DOCTYPE html>
                <html lang="ru">
                <head>
                    <meta charset="UTF-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>Авторизация завершена</title>
                    <style>
                        body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
                        .success { color: green; font-size: 18px; }
                    </style>
                </head>
                <body>
                    <div class="success">
                        <h2>✅ Авторизация завершена успешно!</h2>
                        <p>Вы можете закрыть это окно и вернуться к приложению.</p>
                    </div>
                </body>
                </html>
                """;

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer.AsMemory());
            response.Close();

            return code;
        }
        finally
        {
            listener.Stop();
        }
    }
}
