using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class TwitchOAuthService(
    OAuthFlowCoordinator flow,
    OAuthTokenRefresher refresher,
    OAuthTokenClient tokenClient,
    OAuthAccountWriter accountWriter,
    OAuthStatusReporter statusReporter)
    : ITwitchOAuthService, IPlatformAuthService, IDisposable
{
    private bool _isDisposed;

    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public event Action<PlatformAuthRole, string>? StatusChanged
    {
        add => statusReporter.StatusChanged += value;
        remove => statusReporter.StatusChanged -= value;
    }

    public async Task<string> StartOAuthFlowAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        Action<string> onAuthUrlReady,
        bool checkBroadcasterChannel = true,
        CancellationToken ct = default)
    {
        var token = await flow.StartOAuthFlowAsync(role, clientId, clientSecret, scopes, redirectUri, onAuthUrlReady, checkBroadcasterChannel, ct);
        refresher.ResetRetryState(role);
        return token;
    }

    public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        Action<string> onAuthUrlReady,
        bool checkBroadcasterChannel = true,
        CancellationToken ct = default)
    {
        return flow.StartOAuthFlowToDraftAsync(role, clientId, clientSecret, scopes, redirectUri, onAuthUrlReady, checkBroadcasterChannel, ct);
    }

    public void SetAuthResult(string code, string? state)
    {
        flow.SetAuthResult(code, state);
    }

    public void SetAuthError(Exception exception)
    {
        flow.SetAuthError(exception);
    }

    public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default)
    {
        return tokenClient.IsTokenValidAsync(token, ct);
    }

    public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default)
    {
        return tokenClient.ValidateAsync(token, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return refresher.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public async Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
    {
        return await refresher.GetAccessTokenAsync(role, forceRefresh: false, ct);
    }

    public async Task<string?> GetAccessTokenAsync(PlatformAuthRole role, string? contextId, CancellationToken ct = default)
    {
        return await refresher.GetAccessTokenAsync(role, contextId, forceRefresh: false, ct);
    }

    ValueTask<string?> IPlatformAuthService.GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh,
        CancellationToken ct)
    {
        return refresher.GetAccessTokenAsync(role, forceRefresh, ct);
    }

    ValueTask<string?> IPlatformAuthService.GetAccessTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        bool forceRefresh,
        CancellationToken ct)
    {
        return refresher.GetAccessTokenAsync(role, contextId, forceRefresh, ct);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        return refresher.HasUsableAccessToken(role);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
    {
        return refresher.HasUsableAccessToken(role, contextId);
    }

    public bool? HasAuthorizationMaterial(PlatformAuthRole role, string? contextId) =>
        accountWriter.HasAuthorizationMaterial(role, contextId);

    public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken)
    {
        return accountWriter.TryClearAccessToken(role, failedAccessToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string failedAccessToken)
    {
        return accountWriter.TryClearAccessToken(role, contextId, failedAccessToken);
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        refresher.ResetRetryState(role);
    }

    public void ResetRetryState(PlatformAuthRole role, string? contextId)
    {
        refresher.ResetRetryState(role, contextId);
    }

    public void UpdateSettings(
        PlatformAuthRole role,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false)
    {
        accountWriter.UpdateSettings(role, accessToken, refreshToken, newScopes, login, userId, expiresInSeconds, publishAuthorizationRefreshed);
    }

    public void ClearTokens(PlatformAuthRole role)
    {
        refresher.ResetRetryState(role);
        accountWriter.ClearTokens(role);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        statusReporter.ClearSubscribers();
        _isDisposed = true;
    }
}
