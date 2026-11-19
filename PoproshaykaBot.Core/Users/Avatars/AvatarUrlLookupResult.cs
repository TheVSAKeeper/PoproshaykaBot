namespace PoproshaykaBot.Core.Users.Avatars;

public enum AvatarUrlLookupStatus
{
    None = 0,
    Found = 1,
    NotFound = 2,
    TransientFailure = 3,
}

public readonly record struct AvatarUrlLookupResult(AvatarUrlLookupStatus Status, string? Url)
{
    public static AvatarUrlLookupResult NotFound => new(AvatarUrlLookupStatus.NotFound, null);

    public static AvatarUrlLookupResult TransientFailure => new(AvatarUrlLookupStatus.TransientFailure, null);

    public static AvatarUrlLookupResult Found(string url) => new(AvatarUrlLookupStatus.Found, url);
}
