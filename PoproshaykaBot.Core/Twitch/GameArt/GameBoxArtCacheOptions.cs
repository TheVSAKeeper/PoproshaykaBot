namespace PoproshaykaBot.Core.Twitch.GameArt;

public sealed class GameBoxArtCacheOptions
{
    public const int ImageWidth = 104;
    public const int ImageHeight = 144;

    public static readonly TimeSpan DefaultMissLifetime = TimeSpan.FromDays(7);

    public required string Directory { get; init; }

    public bool CacheOnly { get; init; }

    public TimeSpan MissLifetime { get; init; } = DefaultMissLifetime;

    public int MaxImageBytes { get; init; } = 2 * 1024 * 1024;
}
