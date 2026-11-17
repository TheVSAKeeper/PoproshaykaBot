namespace PoproshaykaBot.Core.Platforms;

public readonly record struct ChannelKey
{
    public ChannelKey(StreamingPlatform platform, string channelId)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        Platform = platform;
        ChannelId = channelId;
    }

    public StreamingPlatform Platform { get; }
    public string ChannelId { get; }

    internal static ChannelKey? FromId(StreamingPlatform platform, string? channelId)
    {
        return string.IsNullOrWhiteSpace(channelId) ? null : new(platform, channelId);
    }
}
