using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthAccountWriter(
    YouTubeAccountsStore accountsStore,
    IEventBus eventBus,
    YouTubeOAuthStatusReporter statusReporter,
    TimeProvider timeProvider,
    ILogger<YouTubeOAuthAccountWriter> logger)
{
    public void SaveFlowResult(
        PlatformAuthRole role,
        YouTubeOAuthFlowResult result,
        bool publishAuthorizationRefreshed = true)
    {
        SaveFlowResult(role, null, result, publishAuthorizationRefreshed);
    }

    public void SaveFlowResult(
        PlatformAuthRole role,
        string? contextId,
        YouTubeOAuthFlowResult result,
        bool publishAuthorizationRefreshed = true)
    {
        ArgumentNullException.ThrowIfNull(result);

        var normalized = AuthContextIds.Normalize(contextId);
        var now = timeProvider.GetUtcNow();

        accountsStore.Mutate(role, normalized, account => result.ApplyTo(account, now));
        logger.LogInformation("Сохранён YouTube OAuth токен для роли {Role} (контекст {ContextId}, канал: {ChannelTitle}, id: {ChannelId})",
            role,
            normalized,
            result.ChannelTitle,
            result.ChannelId);

        statusReporter.Report(role, $"Авторизован как {result.ChannelTitle}");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role, normalized));
        }
    }

    public void ApplyRefreshResult(
        PlatformAuthRole role,
        string accessToken,
        string? refreshToken,
        IReadOnlyList<string> scopes,
        int expiresInSeconds,
        bool publishAuthorizationRefreshed = true)
    {
        ApplyRefreshResult(role, null, accessToken, refreshToken, scopes, expiresInSeconds, publishAuthorizationRefreshed);
    }

    public void ApplyRefreshResult(
        PlatformAuthRole role,
        string? contextId,
        string accessToken,
        string? refreshToken,
        IReadOnlyList<string> scopes,
        int expiresInSeconds,
        bool publishAuthorizationRefreshed = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        var normalized = AuthContextIds.Normalize(contextId);
        var now = timeProvider.GetUtcNow();

        accountsStore.Mutate(role, normalized, account =>
        {
            account.AccessToken = accessToken;

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                account.RefreshToken = refreshToken;
            }

            if (scopes is { Count: > 0 })
            {
                account.StoredScopes = [..scopes];
            }

            account.AccessTokenExpiresAt = expiresInSeconds > 0 ? now.AddSeconds(expiresInSeconds) : null;
        });

        logger.LogDebug("Обновлён YouTube access-токен роли {Role} (контекст {ContextId}, refresh {RefreshAction})",
            role,
            normalized,
            string.IsNullOrWhiteSpace(refreshToken) ? "сохранён прежний" : "обновлён");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role, normalized));
        }
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return TryClearAccessToken(role, null, expectedToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string expectedToken)
    {
        return accountsStore.TryClearAccessToken(role, contextId, expectedToken);
    }

    public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        ClearAccount(role, null, publishAuthorizationRefreshed);
    }

    public void ClearAccount(PlatformAuthRole role, string? contextId, bool publishAuthorizationRefreshed = false)
    {
        var normalized = AuthContextIds.Normalize(contextId);
        accountsStore.Mutate(role, normalized, YouTubeOAuthRoleHelpers.ClearAccountInPlace);
        statusReporter.Report(role, "Авторизация очищена");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role, normalized));
        }
    }
}
