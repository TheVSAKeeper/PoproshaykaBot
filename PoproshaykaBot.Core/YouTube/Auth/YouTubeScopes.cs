namespace PoproshaykaBot.Core.YouTube.Auth;

public static class YouTubeScopes
{
    public const string ForceSsl = "https://www.googleapis.com/auth/youtube.force-ssl";
    public const string ReadOnly = "https://www.googleapis.com/auth/youtube.readonly";

    public static readonly IReadOnlyList<string> BotRequired =
    [
        ForceSsl,
    ];

    public static readonly IReadOnlyList<string> BroadcasterRequired =
    [
        ReadOnly,
    ];

    public static IReadOnlyList<string> RequiredFor(YouTubeOAuthRole role)
    {
        return role switch
        {
            YouTubeOAuthRole.Bot => BotRequired,
            YouTubeOAuthRole.Broadcaster => BroadcasterRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    public static bool IsGranted(YouTubeAccountSettings account, string scope)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.StoredScopes.Contains(scope, StringComparer.Ordinal);
    }

    public static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        return new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
    }
}
