using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public enum PointsTransferSourceKind
{
    None = 0,
    Channel = 1,
    UnattributedPlatform = 2,
}

public readonly record struct PointsTransferSource
{
    private PointsTransferSource(PointsTransferSourceKind kind, StreamingPlatform platform, string? channelId)
    {
        Kind = kind;
        Platform = platform;
        ChannelId = channelId;
    }

    public PointsTransferSourceKind Kind { get; }

    public StreamingPlatform Platform { get; }

    public string? ChannelId { get; }

    public ChannelKey? AsChannelKey => Kind == PointsTransferSourceKind.Channel && !string.IsNullOrWhiteSpace(ChannelId)
        ? new(Platform, ChannelId)
        : null;

    public bool IsValid => Kind switch
    {
        PointsTransferSourceKind.Channel => Enum.IsDefined(Platform) && !string.IsNullOrWhiteSpace(ChannelId),
        PointsTransferSourceKind.UnattributedPlatform => Enum.IsDefined(Platform) && ChannelId is null,
        _ => false,
    };

    public static PointsTransferSource ForChannel(ChannelKey channel)
    {
        if (!channel.IsValid)
        {
            throw new ArgumentException("Канал источника не задан.", nameof(channel));
        }

        return new(PointsTransferSourceKind.Channel, channel.Platform, channel.ChannelId);
    }

    public static PointsTransferSource ForUnattributed(StreamingPlatform platform)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        return new(PointsTransferSourceKind.UnattributedPlatform, platform, null);
    }

    public bool MatchesTarget(ChannelKey target)
    {
        return Kind == PointsTransferSourceKind.Channel
            && Platform == target.Platform
            && string.Equals(ChannelId, target.ChannelId, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Kind switch
        {
            PointsTransferSourceKind.Channel => $"{Platform}:{ChannelId}",
            PointsTransferSourceKind.UnattributedPlatform => $"{Platform}:unattributed",
            _ => "none",
        };
    }
}

public enum PointsTransferScope
{
    None = 0,
    All = 1,
    Filtered = 2,
    Selected = 3,
}

public enum PointsTransferMode
{
    None = 0,
    AllCounters = 1,
    AdjustmentsOnly = 2,
}

public enum PointsTransferSkipReason
{
    None = 0,
    MissingTargetAccountLink = 1,
    AmbiguousTargetAccountLink = 2,
    NothingToTransfer = 3,
}

public sealed record PointsTransferRequest(
    PointsTransferSource Source,
    ChannelKey Target,
    PointsTransferMode Mode,
    IReadOnlyCollection<PlatformUserKey>? SourceUserKeys = null);

public sealed record PointsTransferUserPreview(
    PlatformUserKey SourceUserKey,
    string SourceUserName,
    PlatformUserKey? TargetUserKey,
    string? TargetUserName,
    ulong MessageCount,
    ulong BonusPoints,
    ulong PenaltyPoints,
    PointsTransferSkipReason SkipReason)
{
    public bool WillTransfer => SkipReason == PointsTransferSkipReason.None;

    public long Points => PointsTransferMath.ComputeSignedPoints(MessageCount, BonusPoints, PenaltyPoints);
}

public sealed record PointsTransferPreview(
    PointsTransferSource Source,
    ChannelKey Target,
    PointsTransferMode Mode,
    IReadOnlyList<PointsTransferUserPreview> Users,
    int TransferredCount,
    int SkippedCount,
    ulong TotalMessages,
    ulong TotalBonusPoints,
    ulong TotalPenaltyPoints,
    long TotalPoints);

public sealed record PointsTransferResult(
    PointsTransferSource Source,
    ChannelKey Target,
    PointsTransferMode Mode,
    int TransferredCount,
    int SkippedCount,
    int MissingLinkSkippedCount,
    int AmbiguousLinkSkippedCount,
    ulong TotalMessages,
    ulong TotalBonusPoints,
    ulong TotalPenaltyPoints,
    long TotalPoints);

public sealed record PointsTransferExecutionResult(
    PointsTransferResult Result,
    bool PersistedToDisk,
    string? SaveError = null);

internal static class PointsTransferMath
{
    public static ulong AddSaturating(ulong current, ulong delta)
    {
        return ulong.MaxValue - current < delta ? ulong.MaxValue : current + delta;
    }

    public static long ComputeSignedPoints(ulong messageCount, ulong bonusPoints, ulong penaltyPoints)
    {
        var earned = (Int128)messageCount + bonusPoints - penaltyPoints;
        if (earned > long.MaxValue)
        {
            return long.MaxValue;
        }

        if (earned < long.MinValue)
        {
            return long.MinValue;
        }

        return (long)earned;
    }
}
