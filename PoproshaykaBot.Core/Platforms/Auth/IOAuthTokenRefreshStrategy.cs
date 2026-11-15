namespace PoproshaykaBot.Core.Platforms.Auth;

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

    bool TryReadBackgroundCredentials(out string clientId, out string clientSecret);

    Task<string> RefreshAndSaveAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct);

    OAuthRefreshFailureKind ClassifyFailure(Exception exception);

    Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, Exception exception, CancellationToken ct);

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
