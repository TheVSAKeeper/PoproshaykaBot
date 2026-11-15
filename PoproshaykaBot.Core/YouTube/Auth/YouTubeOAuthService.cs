using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthService(
    YouTubeOAuthFlowCoordinator flowCoordinator,
    YouTubeOAuthTokenRefresher tokenRefresher,
    YouTubeOAuthAccountWriter accountWriter,
    YouTubeOAuthStatusReporter statusReporter)
    : IYouTubeOAuthService, IPlatformAuthService, IDisposable
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public event Action<PlatformAuthRole, string>? StatusChanged
    {
        add => statusReporter.StatusChanged += value;
        remove => statusReporter.StatusChanged -= value;
    }

    public async Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        PlatformAuthRole role,
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
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        return flowCoordinator.StartOAuthFlowToDraftAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return tokenRefresher.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public async Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
    {
        return await tokenRefresher.GetAccessTokenAsync(role, forceRefresh: false, ct);
    }

    ValueTask<string?> IPlatformAuthService.GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh,
        CancellationToken ct)
    {
        return tokenRefresher.GetAccessTokenAsync(role, forceRefresh, ct);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        return tokenRefresher.HasUsableAccessToken(role);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return accountWriter.TryClearAccessToken(role, expectedToken);
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        tokenRefresher.ResetRetryState(role);
    }

    public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        tokenRefresher.ResetRetryState(role);
        accountWriter.ClearAccount(role, publishAuthorizationRefreshed);
    }

    public void Dispose()
    {
        statusReporter.ClearSubscribers();
    }
}
