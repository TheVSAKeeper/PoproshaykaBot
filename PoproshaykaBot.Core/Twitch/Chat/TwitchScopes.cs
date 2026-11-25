using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Chat;

using Platforms.Auth;

public static class TwitchScopes
{
    internal static readonly TimeSpan AccessTokenRefreshMargin = TimeSpan.FromMinutes(5);

    public const string UserReadChat = "user:read:chat";
    public const string UserWriteChat = "user:write:chat";
    public const string UserBot = "user:bot";
    public const string ChannelBot = "channel:bot";
    public const string ChannelManageBroadcast = "channel:manage:broadcast";
    public const string ChannelManagePolls = "channel:manage:polls";
    public const string ChannelReadPolls = "channel:read:polls";
    public const string UserManageWhispers = "user:manage:whispers";

    public static readonly IReadOnlyList<string> BotRequired =
    [
        UserReadChat,
        UserWriteChat,
        UserBot,
    ];

    public static readonly IReadOnlyList<string> BotOptional =
    [
        UserManageWhispers,
    ];

    public static readonly IReadOnlyList<string> BroadcasterRequired =
    [
        ChannelBot,
        ChannelManageBroadcast,
        ChannelManagePolls,
        ChannelReadPolls,
    ];

    public static readonly IReadOnlyList<string> CombinedDefault =
    [
        .. BotRequired,
        .. BotOptional,
        .. BroadcasterRequired,
    ];

    public static bool IsGranted(TwitchAccountSettings account, string scope)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.StoredScopes.Contains(scope, StringComparer.Ordinal);
    }

    public static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        return new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
    }

    public static bool HasUsableAuthorization(PlatformAuthRole role, TwitchAccountSettings? account)
    {
        if (account is null || (string.IsNullOrWhiteSpace(account.RefreshToken)
            && (string.IsNullOrWhiteSpace(account.AccessToken) || account.AccessTokenExpiresAt is not { } expiresAt
                || expiresAt - DateTimeOffset.UtcNow <= AccessTokenRefreshMargin)))
        {
            return false;
        }

        var required = role == PlatformAuthRole.Bot ? BotRequired : BroadcasterRequired;
        return account.StoredScopes is not null
            && required.All(scope => IsGranted(account, scope))
            && SetEquals(account.StoredScopes, account.Scopes is { Length: > 0 } ? account.Scopes : DefaultScopes(role, account));
    }

    internal static IReadOnlyList<string> DefaultScopes(PlatformAuthRole role, TwitchAccountSettings account)
    {
        var required = role == PlatformAuthRole.Bot ? BotRequired : BroadcasterRequired;
        return account.StoredScopes is { } granted && required.All(scope => IsGranted(account, scope)) ? granted : required;
    }
}
