namespace PoproshaykaBot.Core.YouTube.Auth;

public interface IYouTubeOAuthService
{
    event Action<YouTubeOAuthRole, string>? StatusChanged;

    Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default);

    Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default);

    Task<string> RefreshTokenAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default);

    Task<string?> GetAccessTokenAsync(YouTubeOAuthRole role, CancellationToken ct = default);

    bool TryClearAccessToken(YouTubeOAuthRole role, string expectedToken);

    void ClearAccount(YouTubeOAuthRole role, bool publishAuthorizationRefreshed = false);
}
