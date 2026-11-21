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

    Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    void ResetRetryState(PlatformAuthRole role, string? contextId) { }

    Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default);

    Task<string?> GetAccessTokenAsync(PlatformAuthRole role, string? contextId, CancellationToken ct = default)
    {
        return GetAccessTokenAsync(role, ct);
    }

    bool TryClearAccessToken(PlatformAuthRole role, string expectedToken);

    bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string expectedToken)
    {
        return TryClearAccessToken(role, expectedToken);
    }

    void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false);
}
