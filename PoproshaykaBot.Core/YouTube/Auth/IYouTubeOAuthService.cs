using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public interface IYouTubeOAuthService
{
    event Action<PlatformAuthRole, string>? StatusChanged;

    Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default);

    Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default);

    Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default);

    Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default);

    bool TryClearAccessToken(PlatformAuthRole role, string expectedToken);

    void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false);
}
