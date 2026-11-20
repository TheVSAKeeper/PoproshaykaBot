namespace PoproshaykaBot.Core.Platforms.Auth;

public interface IPlatformAuthService
{
    StreamingPlatform Platform { get; }

    ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default);

    ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        return GetAccessTokenAsync(role, forceRefresh, ct);
    }

    bool HasUsableAccessToken(PlatformAuthRole role);

    bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
    {
        return HasUsableAccessToken(role);
    }

    bool? HasAuthorizationMaterial(PlatformAuthRole role, string? contextId) => null;

    bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken);

    bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string failedAccessToken)
    {
        return TryClearAccessToken(role, failedAccessToken);
    }

    void ResetRetryState(PlatformAuthRole role);

    void ResetRetryState(PlatformAuthRole role, string? contextId)
    {
        ResetRetryState(role);
    }
}
