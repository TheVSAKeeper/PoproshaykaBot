using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthAccountWriter(
    AccountsStore accountsStore,
    IEventBus eventBus,
    OAuthStatusReporter statusReporter,
    TimeProvider timeProvider,
    ILogger<OAuthAccountWriter> logger)
{
    public void UpdateSettings(
        PlatformAuthRole role,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false)
    {
        var scopesSnapshot = newScopes?.ToArray();
        var now = timeProvider.GetUtcNow();

        accountsStore.Mutate(role, account =>
        {
            account.AccessToken = accessToken;
            account.RefreshToken = refreshToken;
            account.Login = login;
            account.UserId = userId;

            if (scopesSnapshot != null)
            {
                account.StoredScopes = scopesSnapshot;
            }

            account.AccessTokenExpiresAt = expiresInSeconds > 0
                ? now.AddSeconds(expiresInSeconds)
                : null;
        });

        logger.LogInformation("Токены роли {Role} обновлены в настройках (login={Login}, expiresIn={ExpiresIn}s, scopes={ScopeCount})",
            role,
            login,
            expiresInSeconds,
            scopesSnapshot?.Length ?? -1);

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, role));
        }
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return accountsStore.TryClearAccessToken(role, expectedToken);
    }

    public void ClearTokens(PlatformAuthRole role)
    {
        accountsStore.Mutate(role, OAuthRoleHelpers.ClearAccountInPlace);

        statusReporter.Report(role, "Токены очищены.");
        logger.LogInformation("Токены роли {Role} удалены из настроек", role);
    }
}
