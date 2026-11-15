namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthService(
    YouTubeOAuthFlowCoordinator flowCoordinator,
    YouTubeOAuthTokenRefresher tokenRefresher,
    YouTubeOAuthAccountWriter accountWriter,
    YouTubeOAuthStatusReporter statusReporter)
    : IYouTubeOAuthService, IDisposable
{
    public event Action<YouTubeOAuthRole, string>? StatusChanged
    {
        add => statusReporter.StatusChanged += value;
        remove => statusReporter.StatusChanged -= value;
    }

    public async Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        var result = await flowCoordinator.StartOAuthFlowAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
        tokenRefresher.ResetRetryState(role);
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
        return flowCoordinator.StartOAuthFlowToDraftAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
    }

    public Task<string> RefreshTokenAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return tokenRefresher.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public Task<string?> GetAccessTokenAsync(YouTubeOAuthRole role, CancellationToken ct = default)
    {
        return tokenRefresher.GetAccessTokenAsync(role, ct);
    }

    public bool TryClearAccessToken(YouTubeOAuthRole role, string expectedToken)
    {
        return accountWriter.TryClearAccessToken(role, expectedToken);
    }

    public void ClearAccount(YouTubeOAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        tokenRefresher.ResetRetryState(role);
        accountWriter.ClearAccount(role, publishAuthorizationRefreshed);
    }

    public void Dispose()
    {
        statusReporter.ClearSubscribers();
    }
}
