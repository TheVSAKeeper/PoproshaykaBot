namespace PoproshaykaBot.Core.Platforms.Auth;

public readonly record struct OAuthRefreshResult(string AccessToken, bool Persisted);

public enum OAuthRefreshFailureKind
{
    None = 0,
    Transient = 1,
    RefreshTokenRejected = 2,
    CredentialsRejected = 3,
}

public interface IOAuthTokenRefreshStrategy<in TAccount>
    where TAccount : class, IRoleAccountSettings
{
    StreamingPlatform Platform { get; }

    TimeSpan TokenRefreshMargin { get; }

    TimeSpan BaseTransientRetryDelay { get; }

    TimeSpan MaxTransientRetryDelay { get; }

    TimeSpan CredentialsRejectedCooldown { get; }

    bool RefreshWhenExpirationMissing => false;

    bool AreStoredScopesValid(PlatformAuthRole role, TAccount account);

    Task HandleScopeMismatchAsync(PlatformAuthRole role, TAccount accountSnapshot, CancellationToken ct);

    Task HandleScopeMismatchAsync(PlatformAuthRole role, string contextId, TAccount accountSnapshot, CancellationToken ct)
    {
        return HandleScopeMismatchAsync(role, accountSnapshot, ct);
    }

    bool TryReadBackgroundCredentials(out string clientId, out string clientSecret);

    Task<string> RefreshAndSaveAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct);

    Task<string> RefreshAndSaveAsync(
        PlatformAuthRole role,
        string contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        return RefreshAndSaveAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    async Task<OAuthRefreshResult> RefreshAndSaveWithResultAsync(
        PlatformAuthRole role,
        string contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        var accessToken = await RefreshAndSaveAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
        return new(accessToken, Persisted: true);
    }

    OAuthRefreshFailureKind ClassifyFailure(Exception exception);

    Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, Exception exception, CancellationToken ct);

    Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, string contextId, Exception exception, CancellationToken ct)
    {
        return OnRefreshTokenRejectedAsync(role, exception, ct);
    }

    void OnCredentialsRejected(PlatformAuthRole role, Exception exception, DateTimeOffset pausedUntil);

    void OnTransientFailure(PlatformAuthRole role, Exception exception, DateTimeOffset nextAttemptAt);

    void OnMissingRefreshToken(PlatformAuthRole role)
    {
    }

    void OnTokenReplacedWhileWaiting(PlatformAuthRole role)
    {
    }

    void OnRetryDeferred(PlatformAuthRole role, DateTimeOffset nextAttemptAt, int consecutiveFailures)
    {
    }
}
