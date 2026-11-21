using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Twitch.Auth;

public interface ITwitchOAuthService
{
    event Action<PlatformAuthRole, string>? StatusChanged;

    Task<string> StartOAuthFlowAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        Action<string> onAuthUrlReady,
        bool checkBroadcasterChannel = true,
        CancellationToken ct = default);

    Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        string? redirectUri,
        Action<string> onAuthUrlReady,
        bool checkBroadcasterChannel = true,
        CancellationToken ct = default);

    void SetAuthResult(string code, string? state);

    void SetAuthError(Exception exception);

    Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default);

    Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default);

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

    void UpdateSettings(
        PlatformAuthRole role,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false);

    void ClearTokens(PlatformAuthRole role);
}
