using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public static class YouTubeScopes
{
    internal static readonly TimeSpan AccessTokenRefreshMargin = TimeSpan.FromMinutes(5);

    public const string ForceSsl = "https://www.googleapis.com/auth/youtube.force-ssl";
    public const string ReadOnly = "https://www.googleapis.com/auth/youtube.readonly";

    public static readonly IReadOnlyList<string> BotRequired =
    [
        ForceSsl,
    ];

    public static readonly IReadOnlyList<string> BroadcasterRequired =
    [
        ForceSsl,
    ];

    public static readonly IReadOnlyList<string> CombinedDefault =
    [
        .. BotRequired.Concat(BroadcasterRequired).Distinct(StringComparer.Ordinal),
    ];

    public static IReadOnlyList<string> RequiredFor(PlatformAuthRole role)
    {
        return role switch
        {
            PlatformAuthRole.Bot => BotRequired,
            PlatformAuthRole.Broadcaster => BroadcasterRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    public static bool IsGranted(YouTubeAccountSettings account, string scope)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.StoredScopes.Contains(scope, StringComparer.Ordinal);
    }

    public static bool ContainsRequiredScopes(PlatformAuthRole role, IEnumerable<string> grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(grantedScopes);

        var grantedSet = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        return RequiredFor(role).All(grantedSet.Contains);
    }

    public static bool ContainsUsableScopes(PlatformAuthRole role, IEnumerable<string> grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(grantedScopes);

        var grantedSet = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        if (RequiredFor(role).All(grantedSet.Contains))
        {
            return true;
        }

        return role == PlatformAuthRole.Broadcaster
            && grantedSet.Contains(ReadOnly);
    }

    public static bool IsScopeSatisfied(IReadOnlySet<string> grantedScopes, string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(grantedScopes);

        if (grantedScopes.Contains(requiredScope))
        {
            return true;
        }

        return string.Equals(requiredScope, ReadOnly, StringComparison.OrdinalIgnoreCase)
            && grantedScopes.Contains(ForceSsl);
    }

    public static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        return new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
    }

    public static bool HasUsableAuthorization(PlatformAuthRole role, YouTubeAccountSettings? account, DateTimeOffset? utcNow = null)
    {
        if (!HasUsableToken(account, utcNow))
        {
            return false;
        }

        return account!.StoredScopes is not null
            && ContainsUsableScopes(role, account.StoredScopes);
    }

    public static bool HasUsableToken(YouTubeAccountSettings? account, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        return account is not null
            && (!string.IsNullOrWhiteSpace(account.RefreshToken)
                || (!string.IsNullOrWhiteSpace(account.AccessToken)
                    && account.AccessTokenExpiresAt is { } expiresAt
                    && expiresAt - now > AccessTokenRefreshMargin));
    }
}
