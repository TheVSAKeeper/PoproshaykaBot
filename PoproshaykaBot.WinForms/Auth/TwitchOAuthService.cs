using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Stores;
using PoproshaykaBot.WinForms.Twitch.Chat;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.WinForms.Auth;

public sealed class TwitchOAuthService(
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    IHttpClientFactory httpClientFactory,
    ILogger<TwitchOAuthService> logger,
    IEventBus eventBus)
    : IDisposable
{
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private readonly Dictionary<TwitchOAuthRole, SemaphoreSlim> _authSemaphores = CreatePerRoleSemaphores();
    private readonly Dictionary<TwitchOAuthRole, SemaphoreSlim> _refreshSemaphores = CreatePerRoleSemaphores();
    private readonly Dictionary<string, PendingAuth> _pendingAuths = new(StringComparer.Ordinal);
    private readonly object _pendingAuthsLock = new();

    private bool _isDisposed;

    public event Action<TwitchOAuthRole, string>? StatusChanged;

    public async Task<string> StartOAuthFlowAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes = null,
        string? redirectUri = null,
        CancellationToken ct = default)
    {
        logger.LogDebug("Начало процесса OAuth авторизации для роли {Role} (ClientId: {ClientId})", role, clientId);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ID клиента не может быть пустым", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Секрет клиента не может быть пустым", nameof(clientSecret));
        }

        var semaphore = _authSemaphores[role];
        await semaphore.WaitAsync(ct);

        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingAuthsLock)
        {
            _pendingAuths[state] = new(role, tcs);
        }

        try
        {
            var settings = settingsManager.Current.Twitch;
            scopes ??= accountsStore.Load(role).Scopes;
            redirectUri ??= settings.RedirectUri;

            var scopeString = string.Join(" ", scopes);

            ReportStatus(role, "Открытие браузера для авторизации...");

            var authUrl = "https://id.twitch.tv/oauth2/authorize"
                          + "?response_type=code"
                          + $"&client_id={Uri.EscapeDataString(clientId)}"
                          + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                          + $"&scope={Uri.EscapeDataString(scopeString)}"
                          + $"&state={Uri.EscapeDataString(state)}"
                          + "&force_verify=true";

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true,
                });

                logger.LogInformation("Браузер открыт для авторизации роли {Role} (ClientId: {ClientId})", role, clientId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось открыть браузер для авторизации (роль {Role})", role);
                throw new InvalidOperationException($"Не удалось открыть браузер: {ex.Message}", ex);
            }

            ReportStatus(role, $"Ожидание авторизации пользователя ({AuthTimeout.TotalMinutes} мин)...");

            string authorizationCode;
            try
            {
                authorizationCode = await tcs.Task.WaitAsync(AuthTimeout, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation("OAuth-поток отменён пользователем для роли {Role}", role);
                throw;
            }
            catch (TimeoutException ex)
            {
                logger.LogWarning(ex, "Истекло время ожидания авторизации для роли {Role}", role);
                throw new OperationCanceledException($"Время ожидания авторизации истекло ({AuthTimeout.TotalMinutes} мин)", ex);
            }

            if (string.IsNullOrEmpty(authorizationCode))
            {
                throw new InvalidOperationException("Не удалось получить код авторизации");
            }

            ReportStatus(role, "Обмен кода на токен доступа...");

            var accessToken = await ExchangeCodeForTokenAsync(role, clientId, clientSecret, authorizationCode, redirectUri, ct);

            ReportStatus(role, "Авторизация завершена успешно!");
            logger.LogInformation("OAuth авторизация успешно завершена для роли {Role} (ClientId: {ClientId})", role, clientId);

            return accessToken;
        }
        finally
        {
            lock (_pendingAuthsLock)
            {
                _pendingAuths.Remove(state);
            }

            tcs.TrySetCanceled(ct);
            semaphore.Release();
        }
    }

    public void SetAuthResult(string code, string? state)
    {
        logger.LogDebug("Получен callback авторизации (State: {State})", state);

        if (string.IsNullOrEmpty(state))
        {
            logger.LogWarning("Callback без state-параметра — игнорируется");
            return;
        }

        PendingAuth? pending;
        lock (_pendingAuthsLock)
        {
            _pendingAuths.TryGetValue(state, out pending);
        }

        if (pending == null)
        {
            logger.LogWarning("Callback с неизвестным state '{State}' — возможна CSRF атака или просроченный flow", state);
            return;
        }

        logger.LogInformation("Callback успешно сопоставлен с активным OAuth-потоком для роли {Role}", pending.Role);
        pending.Tcs.TrySetResult(code);
    }

    public void SetAuthError(Exception exception)
    {
        logger.LogError(exception, "Получена ошибка в процессе обработки callback-ответа авторизации");

        lock (_pendingAuthsLock)
        {
            foreach (var pending in _pendingAuths.Values)
            {
                pending.Tcs.TrySetException(exception);
            }
        }
    }

    public async Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default)
    {
        var info = await ValidateAsync(token, ct);
        return info != null;
    }

    public async Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Authorization = new("Bearer", token);

            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Validate-эндпойнт вернул {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var dto = JsonSerializer.Deserialize<ValidateResponse>(json);

            if (dto == null)
            {
                return null;
            }

            return new(dto.Login ?? string.Empty,
                dto.UserId ?? string.Empty,
                dto.ClientId ?? string.Empty,
                dto.Scopes ?? new(),
                dto.ExpiresIn);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Сетевая или внутренняя ошибка при проверке токена Twitch");
            return null;
        }
    }

    public async Task<string> RefreshTokenAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token не может быть пустым", nameof(refreshToken));
        }

        var semaphore = _refreshSemaphores[role];
        await semaphore.WaitAsync(ct);
        try
        {
            return await RefreshTokenInternalAsync(role, clientId, clientSecret, refreshToken, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(TwitchOAuthRole role, CancellationToken ct = default)
    {
        var account = accountsStore.Load(role);

        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return null;
        }

        if (account.StoredScopes.Length > 0
            && !TwitchScopes.SetEquals(account.StoredScopes, account.Scopes))
        {
            HandleScopeMismatch(role, account, ct);
            return null;
        }

        if (account.AccessTokenExpiresAt is { } expiresAt
            && expiresAt - DateTimeOffset.UtcNow > TokenRefreshSkew)
        {
            return account.AccessToken;
        }

        return await RefreshAccessTokenAsync(role, account.AccessToken, ct);
    }

    public void UpdateSettings(
        TwitchOAuthRole role,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false)
    {
        var scopesSnapshot = newScopes?.ToArray();

        accountsStore.Mutate(role, account =>
        {
            account.AccessToken = accessToken;
            account.RefreshToken = refreshToken;
            account.Login = login;
            account.UserId = userId;

            if (scopesSnapshot != null)
            {
                account.StoredScopes = scopesSnapshot;
            }

            account.AccessTokenExpiresAt = expiresInSeconds > 0
                ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
                : null;
        });

        logger.LogInformation("Токены роли {Role} обновлены в настройках (login={Login}, expiresIn={ExpiresIn}s, scopes={ScopeCount})",
            role,
            login,
            expiresInSeconds,
            scopesSnapshot?.Length ?? -1);

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new TwitchAuthorizationRefreshed(role));
        }
    }

    public void ClearTokens(TwitchOAuthRole role)
    {
        accountsStore.Mutate(role, ClearAccountInPlace);

        ReportStatus(role, "Токены очищены.");
        logger.LogInformation("Токены роли {Role} удалены из настроек", role);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        StatusChanged = null;

        foreach (var semaphore in _authSemaphores.Values)
        {
            semaphore.Dispose();
        }

        foreach (var semaphore in _refreshSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _isDisposed = true;
    }

    private static Dictionary<TwitchOAuthRole, SemaphoreSlim> CreatePerRoleSemaphores()
    {
        return new()
        {
            [TwitchOAuthRole.Bot] = new(1, 1),
            [TwitchOAuthRole.Broadcaster] = new(1, 1),
        };
    }

    private static string GetExpectedLogin(TwitchSettings twitch, TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => string.Empty,
            TwitchOAuthRole.Broadcaster => twitch.Channel ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static string DescribeRole(TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => "бот",
            TwitchOAuthRole.Broadcaster => "стример",
            _ => role.ToString(),
        };
    }

    private static void ClearAccountInPlace(TwitchAccountSettings account)
    {
        account.AccessToken = string.Empty;
        account.RefreshToken = string.Empty;
        account.Login = string.Empty;
        account.UserId = string.Empty;
        account.StoredScopes = [];
        account.AccessTokenExpiresAt = null;
    }

    private async Task<string?> RefreshAccessTokenAsync(
        TwitchOAuthRole role,
        string staleToken,
        CancellationToken ct = default)
    {
        var semaphore = _refreshSemaphores[role];
        await semaphore.WaitAsync(ct);
        try
        {
            var settings = settingsManager.Current.Twitch;
            var account = accountsStore.Load(role);

            if (!string.IsNullOrWhiteSpace(account.AccessToken)
                && !string.Equals(account.AccessToken, staleToken, StringComparison.Ordinal))
            {
                return account.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                logger.LogWarning("Refresh-токен для роли {Role} отсутствует, обновление невозможно", role);
                return null;
            }

            try
            {
                return await RefreshTokenInternalAsync(role, settings.ClientId, settings.ClientSecret, account.RefreshToken, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Сбой при обновлении токена роли {Role}", role);
                return null;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void HandleScopeMismatch(TwitchOAuthRole role, TwitchAccountSettings accountSnapshot, CancellationToken ct)
    {
        logger.LogWarning("Scope set роли {Role} изменился — требуется повторная авторизация. Старые: [{Old}]. Новые: [{New}]",
            role,
            string.Join(" ", accountSnapshot.StoredScopes),
            string.Join(" ", accountSnapshot.Scopes));

        var changeMsg = $"Требуется повторная авторизация Twitch ({DescribeRole(role)}): изменился набор прав.";
        ReportStatus(role, changeMsg);
        _ = eventBus.PublishAsync(new BotConnectionStatusUpdated(changeMsg), ct);

        accountsStore.Mutate(role, ClearAccountInPlace);
    }

    private async Task<string> RefreshTokenInternalAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        ReportStatus(role, "Обновление токена доступа...");

        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" },
        };

        var tokenResponse = await PostTokenRequestAsync(formData, ct);
        var validation = await ValidateAsync(tokenResponse.AccessToken, ct);

        if (validation == null)
        {
            throw new InvalidOperationException("Twitch вернул токен, но Validate-эндпойнт его не подтвердил.");
        }

        VerifyLoginMatchesRole(role, validation);

        UpdateSettings(role,
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.Scope,
            validation.Login,
            validation.UserId,
            tokenResponse.ExpiresIn);

        ReportStatus(role, "Токен доступа обновлён успешно!");
        return tokenResponse.AccessToken;
    }

    private void ReportStatus(TwitchOAuthRole role, string status)
    {
        logger.LogInformation("OAuth статус ({Role}): {Status}", role, status);
        StatusChanged?.Invoke(role, status);
    }

    private async Task<string> ExchangeCodeForTokenAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string authorizationCode,
        string redirectUri,
        CancellationToken ct)
    {
        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "code", authorizationCode },
            { "grant_type", "authorization_code" },
            { "redirect_uri", redirectUri },
        };

        var tokenResponse = await PostTokenRequestAsync(formData, ct);
        var validation = await ValidateAsync(tokenResponse.AccessToken, ct);

        if (validation == null)
        {
            throw new InvalidOperationException("Twitch вернул токен, но Validate-эндпойнт его не подтвердил.");
        }

        VerifyLoginMatchesRole(role, validation);

        UpdateSettings(role,
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.Scope,
            validation.Login,
            validation.UserId,
            tokenResponse.ExpiresIn,
            true);

        return tokenResponse.AccessToken;
    }

    private void VerifyLoginMatchesRole(TwitchOAuthRole role, TokenValidationInfo validation)
    {
        var twitch = settingsManager.Current.Twitch;
        var expected = GetExpectedLogin(twitch, role);

        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        if (string.Equals(expected, validation.Login, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var description = DescribeRole(role);
        var message =
            $"Авторизация для роли {description} получила токен пользователя '{validation.Login}', " + $"но в настройках указан канал '{expected}'. " + "Авторизуйтесь под нужной учёткой или исправьте настройку канала. Токен не сохранён.";

        logger.LogError("Логин в полученном токене ({Login}) не совпадает с ожидаемым ({Expected}) для роли {Role}",
            validation.Login, expected, role);

        throw new InvalidOperationException(message);
    }

    private async Task<TokenResponse> PostTokenRequestAsync(Dictionary<string, string> formData, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        const string TokenUrl = "https://id.twitch.tv/oauth2/token";

        using var content = new FormUrlEncodedContent(formData);
        var response = await client.PostAsync(TokenUrl, content, ct);
        var jsonResponse = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Raw OAuth error body: {Body}", jsonResponse);

            var errorStatus = (int)response.StatusCode;
            string? errorMessage = null;
            try
            {
                var errorDto = JsonSerializer.Deserialize<OAuthErrorResponse>(jsonResponse);
                if (errorDto != null)
                {
                    errorStatus = errorDto.Status != 0 ? errorDto.Status : errorStatus;
                    errorMessage = errorDto.Message;
                }
            }
            catch (JsonException)
            {
            }

            logger.LogError("Ошибка HTTP-запроса при получении токена. StatusCode: {StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"OAuth error {errorStatus}: {errorMessage ?? "нет описания"}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Не удалось десериализовать ответ сервера");
        }

        return tokenResponse;
    }

    private sealed record PendingAuth(TwitchOAuthRole Role, TaskCompletionSource<string> Tcs);
}

public record TokenValidationInfo(
    string Login,
    string UserId,
    string ClientId,
    IReadOnlyList<string> Scopes,
    int ExpiresIn);

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

internal sealed record OAuthErrorResponse(
    [property: JsonPropertyName("status")]
    int Status,
    [property: JsonPropertyName("message")]
    string? Message);

internal sealed record ValidateResponse(
    [property: JsonPropertyName("client_id")]
    string? ClientId,
    [property: JsonPropertyName("login")]
    string? Login,
    [property: JsonPropertyName("user_id")]
    string? UserId,
    [property: JsonPropertyName("scopes")]
    List<string>? Scopes,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn);
