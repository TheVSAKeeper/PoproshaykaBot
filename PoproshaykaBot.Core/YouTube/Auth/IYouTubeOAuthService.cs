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

    async Task<OAuthRefreshResult> RefreshTokenWithResultAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        var token = await RefreshTokenAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
        return new(token, Persisted: true);
    }

    Task<YouTubeTokenValidationInfo?> ValidateAsync(string accessToken, CancellationToken ct = default) =>
        Task.FromResult<YouTubeTokenValidationInfo?>(null);

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
