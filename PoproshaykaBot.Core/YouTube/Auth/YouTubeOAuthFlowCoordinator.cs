using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthFlowCoordinator(
    YouTubeOAuthTokenClient tokenClient,
    IYouTubeChannelIdentityClient channelIdentityClient,
    YouTubeOAuthAccountWriter accountWriter,
    YouTubeOAuthStatusReporter statusReporter,
    YouTubeAccountsStore accountsStore,
    YouTubeSettingsStore settingsStore,
    YouTubeOAuthOptions options,
    ILogger<YouTubeOAuthFlowCoordinator> logger) : IDisposable
{
    private readonly SemaphoreSlim _botFlowLock = new(1, 1);
    private readonly SemaphoreSlim _broadcasterFlowLock = new(1, 1);

    public async Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        var result = await StartOAuthFlowCoreAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
        accountWriter.SaveFlowResult(role, result, publishAuthorizationRefreshed: true);
        return result;
    }

    public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        return StartOAuthFlowCoreAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
    }

    public void Dispose()
    {
        _botFlowLock.Dispose();
        _broadcasterFlowLock.Dispose();
    }

    private async Task<YouTubeOAuthFlowResult> StartOAuthFlowCoreAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentNullException.ThrowIfNull(onAuthUrlReady);

        var resolvedClientId = string.IsNullOrWhiteSpace(clientId)
            ? settingsStore.Load().ClientId
            : clientId;

        if (string.IsNullOrWhiteSpace(resolvedClientId))
        {
            throw new YouTubeOAuthException("Не указан Client ID Google-проекта.");
        }

        var resolvedClientSecret = string.IsNullOrWhiteSpace(clientSecret)
            ? accountsStore.LoadClientSecret()
            : clientSecret;

        var flowLock = GetLockForRole(role);
        if (!await flowLock.WaitAsync(TimeSpan.Zero, ct))
        {
            logger.LogInformation("Авторизация YouTube роли {Role} уже выполняется, новый запрос ждёт её завершения", role);
            statusReporter.Report(role, "Для этой роли уже идёт авторизация, ожидание её завершения...");
            await flowLock.WaitAsync(ct);
        }

        try
        {
            var effectiveScopes = ResolveScopes(role, scopes);
            var pkce = PkceCodeChallenge.Create();
            var state = PkceCodeChallenge.CreateRandomToken(24);

            using var listener = new LoopbackOAuthCallbackListener(options.LoopbackConnectionTimeout);
            var authUrl = BuildAuthorizationUrl(
                options.AuthorizationEndpoint,
                resolvedClientId,
                listener.RedirectUri,
                effectiveScopes,
                state,
                pkce.Challenge);

            statusReporter.Report(role, "Ожидание подтверждения входа в системном браузере...");
            logger.LogInformation(
                "Запуск Google Desktop OAuth для роли {Role} с loopback {RedirectUri}",
                role,
                listener.RedirectUri);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(options.AuthTimeout);

            var waitTask = listener.WaitForCallbackAsync(state, timeoutCts.Token);
            onAuthUrlReady(authUrl);

            LoopbackOAuthCallback callback;
            try
            {
                callback = await waitTask;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Время ожидания подтверждения входа в браузере истекло.");
            }

            if (!string.IsNullOrEmpty(callback.Error))
            {
                throw new YouTubeOAuthException(
                    FormatBrowserError(callback.Error, callback.ErrorDescription),
                    callback.Error);
            }

            statusReporter.Report(role, "Обмен кода авторизации на токены...");

            var tokenResponse = await tokenClient.ExchangeCodeAsync(
                resolvedClientId,
                resolvedClientSecret,
                callback.Code!,
                pkce.Verifier,
                listener.RedirectUri,
                ct);

            var channel = await channelIdentityClient.GetOwnChannelAsync(tokenResponse.AccessToken, ct);
            IReadOnlyList<string> grantedScopes = tokenResponse.Scopes.Count > 0 ? tokenResponse.Scopes : effectiveScopes;

            if (tokenResponse.Scopes.Count > 0 && !YouTubeScopes.ContainsRequiredScopes(role, grantedScopes))
            {
                throw new YouTubeOAuthException(
                    "Не выданы обязательные права YouTube. Разрешите доступ к каналу на экране входа Google.",
                    "missing_required_scopes");
            }

            if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                logger.LogWarning(
                    "Google не вернул refresh_token для роли {Role} (канал {ChannelId}) – возможно, приложение уже было разрешено ранее без consent.",
                    role,
                    channel.ChannelId);
            }

            return new(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken ?? string.Empty,
                grantedScopes,
                channel.ChannelId,
                channel.ChannelTitle,
                tokenResponse.ExpiresInSeconds);
        }
        finally
        {
            flowLock.Release();
        }
    }

    private SemaphoreSlim GetLockForRole(PlatformAuthRole role)
    {
        return role switch
        {
            PlatformAuthRole.Bot => _botFlowLock,
            PlatformAuthRole.Broadcaster => _broadcasterFlowLock,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private string[] ResolveScopes(PlatformAuthRole role, string[]? scopes)
    {
        if (scopes is { Length: > 0 })
        {
            return scopes;
        }

        var stored = accountsStore.Load(role).Scopes;
        if (stored is { Length: > 0 })
        {
            return stored;
        }

        return [..YouTubeScopes.RequiredFor(role)];
    }

    private static string BuildAuthorizationUrl(
        string endpoint,
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        string state,
        string codeChallenge)
    {
        var query = string.Join('&',
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_type=code",
            $"scope={Uri.EscapeDataString(string.Join(' ', scopes))}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
            "access_type=offline",
            "prompt=consent");

        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{endpoint}{separator}{query}";
    }

    private static string FormatBrowserError(string error, string? description)
    {
        if (string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Авторизация в Google была отменена пользователем.";
        }

        return string.IsNullOrWhiteSpace(description)
            ? $"Google вернул ошибку авторизации: {error}."
            : $"Google вернул ошибку авторизации ({error}): {description}";
    }
}
