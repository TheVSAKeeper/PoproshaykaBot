namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class TwitchOAuthService(
    OAuthFlowCoordinator flow,
    OAuthTokenRefresher refresher,
    OAuthTokenClient tokenClient,
    OAuthAccountWriter accountWriter,
    OAuthStatusReporter statusReporter)
    : ITwitchOAuthService, IDisposable
{
    private bool _isDisposed;

    public event Action<TwitchOAuthRole, string>? StatusChanged
    {
        add => statusReporter.StatusChanged += value;
        remove => statusReporter.StatusChanged -= value;
    }

    public Task<string> StartOAuthFlowAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        Action<string> onAuthUrlReady,
        bool checkBroadcasterChannel = true,
        CancellationToken ct = default)
    {
        return flow.StartOAuthFlowAsync(role, clientId, clientSecret, scopes, redirectUri, onAuthUrlReady, checkBroadcasterChannel, ct);
    }

    public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
        TwitchOAuthRole role,
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
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return refresher.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public Task<string?> GetAccessTokenAsync(TwitchOAuthRole role, CancellationToken ct = default)
    {
        return refresher.GetAccessTokenAsync(role, ct);
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
        accountWriter.UpdateSettings(role, accessToken, refreshToken, newScopes, login, userId, expiresInSeconds, publishAuthorizationRefreshed);
    }

    public void ClearTokens(TwitchOAuthRole role)
    {
        accountWriter.ClearTokens(role);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        statusReporter.ClearSubscribers();
        flow.Dispose();
        refresher.Dispose();

        _isDisposed = true;
    }
}
