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
        ArgumentNullException.ThrowIfNull(result);

        var now = timeProvider.GetUtcNow();

        accountsStore.Mutate(role, account => result.ApplyTo(account, now));
        logger.LogInformation("Сохранён YouTube OAuth токен для роли {Role} (канал: {ChannelTitle}, id: {ChannelId})",
            role,
            result.ChannelTitle,
            result.ChannelId);

        statusReporter.Report(role, $"Авторизован как {result.ChannelTitle}");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role));
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
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        var now = timeProvider.GetUtcNow();

        accountsStore.Mutate(role, account =>
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

        logger.LogDebug("Обновлён YouTube access-токен роли {Role} (refresh {RefreshAction})",
            role,
            string.IsNullOrWhiteSpace(refreshToken) ? "сохранён прежний" : "обновлён");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role));
        }
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return accountsStore.TryClearAccessToken(role, expectedToken);
    }

    public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        accountsStore.Mutate(role, YouTubeOAuthRoleHelpers.ClearAccountInPlace);
        statusReporter.Report(role, "Авторизация очищена");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, role));
        }
    }
}
