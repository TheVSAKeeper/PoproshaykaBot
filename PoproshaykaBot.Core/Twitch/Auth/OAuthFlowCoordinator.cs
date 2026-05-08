using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using System.Diagnostics;
using System.Security.Cryptography;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthFlowCoordinator(
    OAuthTokenClient tokenClient,
    OAuthAccountWriter accountWriter,
    OAuthStatusReporter statusReporter,
    AccountsStore accountsStore,
    SettingsManager settingsManager,
    ILogger<OAuthFlowCoordinator> logger)
    : IDisposable
{
    private readonly Dictionary<TwitchOAuthRole, SemaphoreSlim> _authSemaphores = new()
    {
        [TwitchOAuthRole.Bot] = new(1, 1),
        [TwitchOAuthRole.Broadcaster] = new(1, 1),
    };

    private readonly Dictionary<string, PendingAuth> _pendingAuths = new(StringComparer.Ordinal);
    private readonly object _pendingAuthsLock = new();

    private bool _isDisposed;

    private TimeSpan AuthTimeout =>
        TimeSpan.FromMinutes(settingsManager.Current.Twitch.Infrastructure.OAuthAuthTimeoutMinutes);

    public async Task<string> StartOAuthFlowAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        CancellationToken ct)
    {
        var result = await StartOAuthFlowCoreAsync(role, clientId, clientSecret, scopes, redirectUri, ct);

        accountWriter.UpdateSettings(role,
            result.AccessToken,
            result.RefreshToken,
            result.Scopes,
            result.Login,
            result.UserId,
            result.ExpiresInSeconds,
            true);

        return result.AccessToken;
    }

    public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        CancellationToken ct)
    {
        return StartOAuthFlowCoreAsync(role, clientId, clientSecret, scopes, redirectUri, ct);
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var semaphore in _authSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _isDisposed = true;
    }

    private async Task<OAuthFlowResult> StartOAuthFlowCoreAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        CancellationToken ct)
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
            var authTimeout = AuthTimeout;
            scopes ??= accountsStore.Load(role).Scopes;
            redirectUri ??= settings.RedirectUri;

            var scopeString = string.Join(" ", scopes);

            statusReporter.Report(role, "Открытие браузера для авторизации...");

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

            statusReporter.Report(role, $"Ожидание авторизации пользователя ({authTimeout.TotalMinutes} мин)...");

            string authorizationCode;
            try
            {
                authorizationCode = await tcs.Task.WaitAsync(authTimeout, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation("OAuth-поток отменён пользователем для роли {Role}", role);
                throw;
            }
            catch (TimeoutException ex)
            {
                logger.LogWarning(ex, "Истекло время ожидания авторизации для роли {Role}", role);
                throw new OperationCanceledException($"Время ожидания авторизации истекло ({authTimeout.TotalMinutes} мин)", ex);
            }

            if (string.IsNullOrEmpty(authorizationCode))
            {
                throw new InvalidOperationException("Не удалось получить код авторизации");
            }

            statusReporter.Report(role, "Обмен кода на токен доступа...");

            var result = await ExchangeCodeForTokenAsync(role, clientId, clientSecret, authorizationCode, redirectUri, ct);

            statusReporter.Report(role, "Авторизация завершена успешно!");
            logger.LogInformation("OAuth авторизация успешно завершена для роли {Role} (ClientId: {ClientId})", role, clientId);

            return result;
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

    private async Task<OAuthFlowResult> ExchangeCodeForTokenAsync(
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

        var tokenResponse = await tokenClient.PostTokenRequestAsync(formData, ct);
        var validation = await tokenClient.ValidateAsync(tokenResponse.AccessToken, ct);

        if (validation == null)
        {
            throw new InvalidOperationException("Twitch вернул токен, но Validate-эндпойнт его не подтвердил.");
        }

        OAuthRoleHelpers.VerifyLoginMatchesRole(role, validation, settingsManager, logger);

        return new(tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.Scope?.ToArray() ?? Array.Empty<string>(),
            validation.Login,
            validation.UserId,
            tokenResponse.ExpiresIn);
    }

    private sealed record PendingAuth(TwitchOAuthRole Role, TaskCompletionSource<string> Tcs);
}
