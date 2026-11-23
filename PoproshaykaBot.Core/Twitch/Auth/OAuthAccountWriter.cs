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
        UpdateSettings(
            role,
            null,
            accessToken,
            refreshToken,
            newScopes,
            login,
            userId,
            expiresInSeconds,
            publishAuthorizationRefreshed);
    }

    public bool UpdateSettings(
        PlatformAuthRole role,
        string? contextId,
        string accessToken,
        string refreshToken,
        IEnumerable<string>? newScopes,
        string login,
        string userId,
        int expiresInSeconds = 0,
        bool publishAuthorizationRefreshed = false,
        string? previousRefreshToken = null)
    {
        var normalized = AuthContextIds.Normalize(contextId);
        var scopesSnapshot = newScopes?.ToArray();
        var now = timeProvider.GetUtcNow();
        DateTimeOffset? expiresAt = expiresInSeconds > 0
            ? now.AddSeconds(expiresInSeconds)
            : null;

        void ApplyFields(TwitchAccountSettings account)
        {
            account.AccessToken = accessToken;
            account.RefreshToken = refreshToken;
            account.Login = login;
            if (!string.Equals(account.UserId, userId, StringComparison.Ordinal))
            {
                account.BrowserProfileName = null;
            }

            account.UserId = userId;

            if (scopesSnapshot != null)
            {
                account.StoredScopes = [.. scopesSnapshot];
            }

            account.AccessTokenExpiresAt = expiresAt;
        }

        bool applied;
        bool written;
        IReadOnlyList<(PlatformAuthRole Role, string ContextId)> syncedSiblings;
        if (!string.IsNullOrWhiteSpace(previousRefreshToken))
        {
            var sync = accountsStore.MutateAndSyncSharedRefreshToken(
                role,
                normalized,
                previousRefreshToken,
                ApplyFields,
                ApplyFields,
                expectedSubjectId: userId);
            applied = sync.Applied;
            written = sync.Written;
            syncedSiblings = sync.SyncedSiblings;
        }
        else
        {
            applied = true;
            written = accountsStore.Mutate(role, normalized, ApplyFields);
            syncedSiblings = [];
        }

        if (!applied)
        {
            logger.LogDebug("Обновление токенов роли {Role} (контекст {ContextId}) пропущено: исходная авторизация уже изменена или удалена",
                role,
                normalized);
            return false;
        }

        logger.LogInformation("Токены роли {Role} (контекст {ContextId}) обновлены в настройках (login={Login}, expiresIn={ExpiresIn}s, scopes={ScopeCount})",
            role,
            normalized,
            login,
            expiresInSeconds,
            scopesSnapshot?.Length ?? -1);

        if (publishAuthorizationRefreshed)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, role, normalized));
        }

        foreach (var sibling in syncedSiblings)
        {
            _ = eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, sibling.Role, sibling.ContextId));
        }

        return written;
    }

    internal bool HasAuthorizationMaterial(PlatformAuthRole role, string? contextId)
    {
        var account = accountsStore.Load(role, contextId);
        return PlatformAuthRoles.HasCredentials(account.AccessToken, account.RefreshToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return TryClearAccessToken(role, null, expectedToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string expectedToken)
    {
        return accountsStore.TryClearAccessToken(role, contextId, expectedToken);
    }

    public void ClearTokens(PlatformAuthRole role)
    {
        ClearTokens(role, null);
    }

    public void ClearTokens(PlatformAuthRole role, string? contextId)
    {
        var normalized = AuthContextIds.Normalize(contextId);
        accountsStore.Mutate(role, normalized, OAuthRoleHelpers.ClearAccountInPlace);

        statusReporter.Report(role, "Токены очищены.");
        logger.LogInformation("Токены роли {Role} (контекст {ContextId}) удалены из настроек", role, normalized);
    }
}
