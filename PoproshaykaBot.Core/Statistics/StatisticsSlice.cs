using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public enum StatisticsSliceKind
{
    None = 0,
    Channel = 1,
    ChannelGroup = 2,
    AllChannels = 3,
    Platform = 4,
    UnattributedPlatform = 5,
}

public sealed class StatisticsSlice
{
    private StatisticsSlice(
        StatisticsSliceKind kind,
        ChannelKey? channel = null,
        StreamingPlatform? platform = null,
        string? groupId = null,
        string? groupName = null,
        IReadOnlySet<ChannelKey>? groupChannels = null)
    {
        Kind = kind;
        Channel = channel;
        Platform = platform;
        GroupId = groupId;
        GroupName = groupName;
        GroupChannels = groupChannels ?? new HashSet<ChannelKey>();
    }

    public StatisticsSliceKind Kind { get; }
    public ChannelKey? Channel { get; }
    public StreamingPlatform? Platform { get; }
    public string? GroupId { get; }
    public string? GroupName { get; }
    public IReadOnlySet<ChannelKey> GroupChannels { get; }

    public static StatisticsSlice ForChannel(ChannelKey channel)
    {
        return new(StatisticsSliceKind.Channel, channel: channel, platform: channel.Platform);
    }

    public static StatisticsSlice ForGroup(string groupId, string groupName, IEnumerable<ChannelKey> channels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentNullException.ThrowIfNull(channels);

        return new(
            StatisticsSliceKind.ChannelGroup,
            groupId: groupId,
            groupName: groupName,
            groupChannels: channels.ToHashSet());
    }

    public static StatisticsSlice All() => new(StatisticsSliceKind.AllChannels);

    public static StatisticsSlice ForPlatform(StreamingPlatform platform)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        return new(StatisticsSliceKind.Platform, platform: platform);
    }

    public static StatisticsSlice Unattributed(StreamingPlatform? platform = null)
    {
        if (platform.HasValue && !Enum.IsDefined(platform.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        return new(StatisticsSliceKind.UnattributedPlatform, platform: platform);
    }
}

public enum ViewerIdentityKind
{
    None = 0,
    Standalone = 1,
    LinkedGroup = 2,
}

public readonly record struct ViewerIdentityKey
{
    private ViewerIdentityKey(ViewerIdentityKind kind, Guid groupId, PlatformUserKey standaloneUser)
    {
        Kind = kind;
        GroupId = groupId;
        StandaloneUser = standaloneUser;
    }

    public ViewerIdentityKind Kind { get; }
    public Guid GroupId { get; }
    public PlatformUserKey StandaloneUser { get; }

    public static ViewerIdentityKey FromGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор группы связей не может быть пустым.", nameof(groupId));
        }

        return new(ViewerIdentityKind.LinkedGroup, groupId, default);
    }

    public static ViewerIdentityKey FromUser(PlatformUserKey userKey)
    {
        return new(ViewerIdentityKind.Standalone, Guid.Empty, userKey);
    }

    public override int GetHashCode()
    {
        return Kind switch
        {
            ViewerIdentityKind.LinkedGroup => HashCode.Combine(Kind, GroupId),
            ViewerIdentityKind.Standalone => HashCode.Combine(Kind, StandaloneUser),
            _ => (int)Kind,
        };
    }

    public bool Equals(ViewerIdentityKey other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            ViewerIdentityKind.LinkedGroup => GroupId == other.GroupId,
            ViewerIdentityKind.Standalone => StandaloneUser == other.StandaloneUser,
            _ => true,
        };
    }

    public override string ToString()
    {
        return Kind == ViewerIdentityKind.LinkedGroup
            ? $"group:{GroupId:D}"
            : $"user:{StandaloneUser}";
    }
}

public sealed record ViewerAccountInfo(
    PlatformUserKey Key,
    string Name,
    DateTime FirstSeen,
    DateTime LastSeen,
    AccountLinkOrigin LinkOrigin = AccountLinkOrigin.None,
    AccountLinkAuthor? LinkAuthor = null,
    DateTimeOffset? LinkedAtUtc = null);

public sealed record ViewerChannelBreakdown(
    StatisticsCounterTarget Target,
    ulong MessageCount,
    ulong BonusPoints,
    ulong PenaltyPoints,
    DateTime FirstSeen,
    DateTime LastSeen)
{
    public long Points => (long)MessageCount + (long)BonusPoints - (long)PenaltyPoints;
}

public sealed record ViewerAggregateStatistics(
    ViewerIdentityKey IdentityKey,
    PlatformUserKey PrimaryKey,
    string DisplayName,
    ulong MessageCount,
    ulong BonusPoints,
    ulong PenaltyPoints,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<ViewerAccountInfo> Accounts,
    IReadOnlyList<ViewerChannelBreakdown> ChannelBreakdowns,
    Guid? LinkGroupId)
{
    public long Points => (long)MessageCount + (long)BonusPoints - (long)PenaltyPoints;

    public bool IsLinked => LinkGroupId.HasValue && Accounts.Count >= 2;

    public bool MatchesAccount(PlatformUserKey key)
    {
        return PrimaryKey == key || Accounts.Any(account => account.Key == key);
    }
}
