using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed record YouTubeTokenResponse(
    string AccessToken,
    string? RefreshToken,
    int ExpiresInSeconds,
    string[] Scopes);

public sealed record YouTubeChannelIdentity(
    string ChannelId,
    string ChannelTitle);

public sealed class YouTubeOAuthTokenClient(
    IHttpClientFactory httpClientFactory,
    YouTubeOAuthOptions options,
    ILogger<YouTubeOAuthTokenClient> logger)
{
    public async Task<YouTubeTokenResponse> ExchangeCodeAsync(
        string clientId,
        string clientSecret,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken ct = default)
    {
        var payload = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("code", code),
            new("code_verifier", codeVerifier),
            new("grant_type", "authorization_code"),
            new("redirect_uri", redirectUri),
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            payload.Add(new("client_secret", clientSecret));
        }

        return await PostTokenRequestAsync(payload, "обмен кода авторизации", ct);
    }

    public async Task<YouTubeTokenResponse> RefreshAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        var payload = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            payload.Add(new("client_secret", clientSecret));
        }

        return await PostTokenRequestAsync(payload, "обновление access-токена", ct);
    }

    public async Task<YouTubeChannelIdentity> GetOwnChannelAsync(string accessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var client = httpClientFactory.CreateClient(YouTubeOAuthOptions.HttpClientName);
        var separator = options.ChannelsEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var requestUri = $"{options.ChannelsEndpoint}{separator}part=snippet&mine=true";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var isRejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            throw new YouTubeOAuthException(
                $"YouTube Data API вернул {(int)response.StatusCode} при чтении канала.",
                errorCode: null,
                statusCode: response.StatusCode,
                isPermanentRejection: isRejected);
        }

        var dto = JsonSerializer.Deserialize<YouTubeChannelsResponseDto>(content);
        var channel = dto?.Items?.FirstOrDefault();

        if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
        {
            throw new YouTubeOAuthException("У выбранного Google-аккаунта не найден канал YouTube.");
        }

        return new(channel.Id, channel.Snippet?.Title ?? channel.Id);
    }

    private async Task<YouTubeTokenResponse> PostTokenRequestAsync(
        IReadOnlyCollection<KeyValuePair<string, string>> formFields,
        string operation,
        CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(YouTubeOAuthOptions.HttpClientName);
        using var content = new FormUrlEncodedContent(formFields);

        using var response = await client.PostAsync(options.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryParseError(body);
            var errorCode = error?.Error;
            var permanent = IsPermanentRejection(response.StatusCode, errorCode);

            logger.LogWarning(
                "Google OAuth ошибка при {Operation}: status={StatusCode}, error={ErrorCode}, description={Description}",
                operation,
                (int)response.StatusCode,
                errorCode ?? "-",
                error?.ErrorDescription ?? "-");

            throw new YouTubeOAuthException(
                FormatErrorMessage(operation, response.StatusCode, errorCode, error?.ErrorDescription),
                errorCode,
                response.StatusCode,
                permanent);
        }

        var token = JsonSerializer.Deserialize<GoogleTokenDto>(body);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new YouTubeOAuthException($"Google OAuth не вернул access_token при операции: {operation}.");
        }

        var scopes = string.IsNullOrWhiteSpace(token.Scope)
            ? []
            : token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new(token.AccessToken, token.RefreshToken, token.ExpiresIn, scopes);
    }

    private static bool IsPermanentRejection(HttpStatusCode statusCode, string? errorCode)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return true;
        }

        return string.Equals(errorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(errorCode, "invalid_client", StringComparison.OrdinalIgnoreCase)
            || string.Equals(errorCode, "unauthorized_client", StringComparison.OrdinalIgnoreCase)
            || string.Equals(errorCode, "deleted_client", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatErrorMessage(string operation, HttpStatusCode statusCode, string? errorCode, string? description)
    {
        if (string.Equals(errorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase))
        {
            return "Срок действия refresh-токена YouTube истёк или доступ был отозван. Повторите авторизацию.";
        }

        if (string.Equals(errorCode, "invalid_client", StringComparison.OrdinalIgnoreCase))
        {
            return "Google отклонил Client ID или Client Secret. Проверьте реквизиты Desktop-клиента в настройках.";
        }

        var suffix = string.IsNullOrWhiteSpace(errorCode) ? $"{(int)statusCode}" : errorCode;
        return string.IsNullOrWhiteSpace(description)
            ? $"Ошибка Google OAuth при {operation}: {suffix}."
            : $"Ошибка Google OAuth при {operation} ({suffix}): {description}";
    }

    private static GoogleOAuthErrorDto? TryParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GoogleOAuthErrorDto>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class GoogleTokenDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class GoogleOAuthErrorDto
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private sealed class YouTubeChannelsResponseDto
    {
        [JsonPropertyName("items")]
        public List<YouTubeChannelItemDto>? Items { get; set; }
    }

    private sealed class YouTubeChannelItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public YouTubeChannelSnippetDto? Snippet { get; set; }
    }

    private sealed class YouTubeChannelSnippetDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
