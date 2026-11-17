using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public readonly record struct ChannelUserKey
{
    public ChannelUserKey(ChannelKey channel, PlatformUserKey user)
    {
        if (channel.Platform != user.Platform)
        {
            throw new ArgumentException("Площадка канала и площадка аккаунта зрителя должны совпадать.", nameof(user));
        }

        Channel = channel;
        User = user;
    }

    public ChannelKey Channel { get; }
    public PlatformUserKey User { get; }

    public override string ToString() => $"{Channel.Platform}:{Channel.ChannelId}/{User.UserId}";
}

public enum StatisticsCounterTargetKind
{
    None = 0,
    Channel = 1,
    UnattributedPlatform = 2,
}

public readonly record struct StatisticsCounterTarget
{
    private StatisticsCounterTarget(StatisticsCounterTargetKind kind, StreamingPlatform platform, string? channelId)
    {
        Kind = kind;
        Platform = platform;
        ChannelId = channelId;
    }

    public StatisticsCounterTargetKind Kind { get; }
    public StreamingPlatform Platform { get; }
    public string? ChannelId { get; }

    public ChannelKey? AsChannelKey => Kind == StatisticsCounterTargetKind.Channel
        ? new(Platform, ChannelId!)
        : null;

    public static StatisticsCounterTarget ForChannel(ChannelKey channel)
    {
        return new(StatisticsCounterTargetKind.Channel, channel.Platform, channel.ChannelId);
    }

    public static StatisticsCounterTarget ForUnattributed(StreamingPlatform platform)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        return new(StatisticsCounterTargetKind.UnattributedPlatform, platform, null);
    }
}
