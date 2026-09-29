namespace PoproshaykaBot.Core.Users.Avatars;

public sealed class UserAvatarCacheOptions
{
    public const long DefaultMaxTotalBytes = 32L * 1024 * 1024;
    public const int DefaultMaxEntries = 5000;

    public static readonly TimeSpan DefaultUrlLifetime = TimeSpan.FromDays(3);
    public static readonly TimeSpan DefaultMissLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultImageLifetime = TimeSpan.FromDays(90);

    public required string Directory { get; init; }

    public bool CacheOnly { get; init; }

    public TimeSpan UrlLifetime { get; init; } = DefaultUrlLifetime;

    public TimeSpan MissLifetime { get; init; } = DefaultMissLifetime;

    public TimeSpan ImageLifetime { get; init; } = DefaultImageLifetime;

    public long MaxTotalBytes { get; init; } = DefaultMaxTotalBytes;

    public int MaxEntries { get; init; } = DefaultMaxEntries;

    public int MaxImageBytes { get; init; } = 1024 * 1024;
}
