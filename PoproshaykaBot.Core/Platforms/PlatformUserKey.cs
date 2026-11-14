namespace PoproshaykaBot.Core.Platforms;

public readonly record struct PlatformUserKey : IEquatable<PlatformUserKey>
{
    public StreamingPlatform Platform { get; }
    public string UserId { get; }

    public PlatformUserKey(StreamingPlatform platform, string userId)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Platform = platform;
        UserId = userId;
    }

    public bool Equals(PlatformUserKey other)
    {
        return Platform == other.Platform && string.Equals(UserId, other.UserId, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Platform, StringComparer.Ordinal.GetHashCode(UserId));
    }

    public override string ToString()
    {
        return $"{Platform}:{UserId}";
    }
}
