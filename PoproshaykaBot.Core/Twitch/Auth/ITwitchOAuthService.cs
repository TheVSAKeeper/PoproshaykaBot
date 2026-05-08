namespace PoproshaykaBot.Core.Twitch.Auth;

public interface ITwitchOAuthService
{
    event Action<TwitchOAuthRole, string>? StatusChanged;

    Task<string> StartOAuthFlowAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes = null,
        string? redirectUri = null,
        CancellationToken ct = default);

    Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes = null,
        string? redirectUri = null,
        CancellationToken ct = default);

    void SetAuthResult(string code, string? state);

    void SetAuthError(Exception exception);

    Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default);

    Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default);

    Task<string> RefreshTokenAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default);

    Task<string?> GetAccessTokenAsync(TwitchOAuthRole role, CancellationToken ct = default);

    void UpdateSettings(
        TwitchOAuthRole role,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false);

    void ClearTokens(TwitchOAuthRole role);
}
