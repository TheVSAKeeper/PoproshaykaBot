using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthService(
    YouTubeOAuthFlowCoordinator flowCoordinator,
    YouTubeOAuthTokenRefresher tokenRefresher,
    YouTubeOAuthAccountWriter accountWriter,
    YouTubeOAuthStatusReporter statusReporter,
    YouTubeOAuthTokenClient? tokenClient = null,
    IYouTubeChannelIdentityClient? channelIdentityClient = null)
    : IYouTubeOAuthService, IPlatformAuthService, IDisposable
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public event Action<PlatformAuthRole, string>? StatusChanged
    {
        add => statusReporter.StatusChanged += value;
        remove => statusReporter.StatusChanged -= value;
    }

    public async Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        var result = await flowCoordinator.StartOAuthFlowAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
        tokenRefresher.ResetRetryState(role);
        return result;
    }

    public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string[]? scopes,
        Action<string> onAuthUrlReady,
        CancellationToken ct = default)
    {
        return flowCoordinator.StartOAuthFlowToDraftAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return tokenRefresher.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return tokenRefresher.RefreshTokenAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
    }

    public Task<OAuthRefreshResult> RefreshTokenWithResultAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return tokenRefresher.RefreshTokenWithResultAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
    }

    public async Task<YouTubeTokenValidationInfo?> ValidateAsync(string accessToken, CancellationToken ct = default)
    {
        if (tokenClient is null)
        {
            return null;
        }

        var info = await tokenClient.ValidateTokenAsync(accessToken, ct);
        if (info is null)
        {
            return null;
        }

        if (channelIdentityClient is not null)
        {
            try
            {
                var channel = await channelIdentityClient.GetOwnChannelAsync(accessToken, ct);
                return info with { ChannelId = channel.ChannelId, ChannelTitle = channel.ChannelTitle };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (YouTubeOAuthException)
            {
                return info;
            }
            catch (HttpRequestException)
            {
                return info;
            }
        }

        return info;
    }

    public async Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
    {
        return await tokenRefresher.GetAccessTokenAsync(role, forceRefresh: false, ct);
    }

    public async Task<string?> GetAccessTokenAsync(PlatformAuthRole role, string? contextId, CancellationToken ct = default)
    {
        return await tokenRefresher.GetAccessTokenAsync(role, contextId, forceRefresh: false, ct);
    }

    ValueTask<string?> IPlatformAuthService.GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh,
        CancellationToken ct)
    {
        return tokenRefresher.GetAccessTokenAsync(role, forceRefresh, ct);
    }

    ValueTask<string?> IPlatformAuthService.GetAccessTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        bool forceRefresh,
        CancellationToken ct)
    {
        return tokenRefresher.GetAccessTokenAsync(role, contextId, forceRefresh, ct);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        return tokenRefresher.HasUsableAccessToken(role);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
    {
        return tokenRefresher.HasUsableAccessToken(role, contextId);
    }

    public bool? HasAuthorizationMaterial(PlatformAuthRole role, string? contextId) =>
        accountWriter.HasAuthorizationMaterial(role, contextId);

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return accountWriter.TryClearAccessToken(role, expectedToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string expectedToken)
    {
        return accountWriter.TryClearAccessToken(role, contextId, expectedToken);
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        tokenRefresher.ResetRetryState(role);
    }

    public void ResetRetryState(PlatformAuthRole role, string? contextId)
    {
        tokenRefresher.ResetRetryState(role, contextId);
    }

    public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        tokenRefresher.ResetRetryState(role);
        accountWriter.ClearAccount(role, publishAuthorizationRefreshed);
    }

    public void Dispose()
    {
        statusReporter.ClearSubscribers();
    }
}
