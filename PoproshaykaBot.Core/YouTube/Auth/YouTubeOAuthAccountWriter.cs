using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthAccountWriter(
    YouTubeAccountsStore accountsStore,
    IEventBus eventBus,
    YouTubeOAuthStatusReporter statusReporter,
    YouTubeOAuthOptions options,
    ILogger<YouTubeOAuthAccountWriter> logger)
{
    public void SaveFlowResult(
        YouTubeOAuthRole role,
        YouTubeOAuthFlowResult result,
        bool publishAuthorizationRefreshed = true)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = options.TimeProvider.GetUtcNow();

        accountsStore.Mutate(role, account => result.ApplyTo(account, now));
        logger.LogInformation("Сохранён YouTube OAuth токен для роли {Role} (канал: {ChannelTitle}, id: {ChannelId})",
            role,
            result.ChannelTitle,
            result.ChannelId);

        statusReporter.Report(role, $"Авторизован как {result.ChannelTitle}");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new YouTubeAuthorizationRefreshed(role));
        }
    }

    public void ApplyRefreshResult(
        YouTubeOAuthRole role,
        string accessToken,
        string? refreshToken,
        string[] scopes,
        int expiresInSeconds,
        bool publishAuthorizationRefreshed = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        var now = options.TimeProvider.GetUtcNow();

        accountsStore.Mutate(role, account =>
        {
            account.AccessToken = accessToken;

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                account.RefreshToken = refreshToken;
            }

            if (scopes is { Length: > 0 })
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
            _ = eventBus.PublishAsync(new YouTubeAuthorizationRefreshed(role));
        }
    }

    public bool TryClearAccessToken(YouTubeOAuthRole role, string expectedToken)
    {
        return accountsStore.TryClearAccessToken(role, expectedToken);
    }

    public void ClearAccount(YouTubeOAuthRole role, bool publishAuthorizationRefreshed = false)
    {
        accountsStore.Mutate(role, YouTubeOAuthRoleHelpers.ClearAccountInPlace);
        statusReporter.Report(role, "Авторизация очищена");

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new YouTubeAuthorizationRefreshed(role));
        }
    }
}
