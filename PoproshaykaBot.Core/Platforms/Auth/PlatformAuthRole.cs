namespace PoproshaykaBot.Core.Platforms.Auth;

public enum PlatformAuthRole
{
    None = 0,
    Bot = 1,
    Broadcaster = 2,
}

public static class PlatformAuthRoles
{
    public static IReadOnlyList<PlatformAuthRole> Active { get; } =
    [
        PlatformAuthRole.Bot,
        PlatformAuthRole.Broadcaster,
    ];

    public static string Describe(PlatformAuthRole role)
    {
        return role switch
        {
            PlatformAuthRole.Bot => "бот",
            PlatformAuthRole.Broadcaster => "стример",
            _ => role.ToString(),
        };
    }

    public static void EnsureActive(PlatformAuthRole role)
    {
        if (role is not (PlatformAuthRole.Bot or PlatformAuthRole.Broadcaster))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, null);
        }
    }
}
