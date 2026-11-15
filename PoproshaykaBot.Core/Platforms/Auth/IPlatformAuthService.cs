namespace PoproshaykaBot.Core.Platforms.Auth;

public interface IPlatformAuthService
{
    StreamingPlatform Platform { get; }

    ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default);

    bool HasUsableAccessToken(PlatformAuthRole role);

    bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken);

    void ResetRetryState(PlatformAuthRole role);
}
