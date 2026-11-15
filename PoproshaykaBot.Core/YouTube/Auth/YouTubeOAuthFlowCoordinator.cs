using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthFlowCoordinator(
    YouTubeOAuthTokenClient tokenClient,
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
        YouTubeOAuthRole role,
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
        YouTubeOAuthRole role,
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
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct)
    {
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
        await flowLock.WaitAsync(ct);

        try
        {
            var effectiveScopes = ResolveScopes(role, scopes);
            var (codeVerifier, codeChallenge) = CreatePkcePair();
            var state = CreateRandomBase64Url(24);

            using var listener = new YouTubeLoopbackListener(options.LoopbackConnectionTimeout);
            var authUrl = BuildAuthorizationUrl(
                options.AuthorizationEndpoint,
                resolvedClientId,
                listener.RedirectUri,
                effectiveScopes,
                state,
                codeChallenge);

            statusReporter.Report(role, "Ожидание подтверждения входа в системном браузере...");
            logger.LogInformation(
                "Запуск Google Desktop OAuth для роли {Role} с loopback {RedirectUri}",
                role,
                listener.RedirectUri);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(options.AuthTimeout);

            var waitTask = listener.WaitForCallbackAsync(state, timeoutCts.Token);
            onAuthUrlReady(authUrl);

            YouTubeLoopbackCallback callback;
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
                codeVerifier,
                listener.RedirectUri,
                ct);

            var channel = await tokenClient.GetOwnChannelAsync(tokenResponse.AccessToken, ct);
            var grantedScopes = tokenResponse.Scopes.Length > 0 ? tokenResponse.Scopes : effectiveScopes;

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

    private SemaphoreSlim GetLockForRole(YouTubeOAuthRole role)
    {
        return role switch
        {
            YouTubeOAuthRole.Bot => _botFlowLock,
            YouTubeOAuthRole.Broadcaster => _broadcasterFlowLock,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private string[] ResolveScopes(YouTubeOAuthRole role, string[]? scopes)
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

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = CreateRandomBase64Url(32);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64Url.EncodeToString(hash);
        return (verifier, challenge);
    }

    private static string CreateRandomBase64Url(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64Url.EncodeToString(bytes);
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
