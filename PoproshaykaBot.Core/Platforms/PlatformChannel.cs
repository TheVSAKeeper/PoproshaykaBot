namespace PoproshaykaBot.Core.Platforms;

public readonly record struct PlatformChannel : IEquatable<PlatformChannel>
{
    public StreamingPlatform Platform { get; }
    public string Channel { get; }

    public PlatformChannel(StreamingPlatform platform, string channel)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        Platform = platform;
        Channel = channel;
    }

    public bool Equals(PlatformChannel other)
    {
        return Platform == other.Platform && string.Equals(Channel, other.Channel, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Platform, StringComparer.OrdinalIgnoreCase.GetHashCode(Channel));
    }

    public override string ToString()
    {
        return $"{Platform}:{Channel}";
    }
}
