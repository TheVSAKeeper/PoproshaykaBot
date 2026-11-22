using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Statistics;

public sealed record BroadcastHistoryRecentItemSummary(
    Guid Id,
    IReadOnlyList<StreamingPlatform> Platforms,
    string? PrimaryChannelId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int SegmentCount,
    TimeSpan SegmentsTotalDuration,
    TimeSpan? TrackedDuration,
    bool IsHidden);

public sealed record BroadcastHistoryDiagnosticsSummary(
    FileReadState SourceReadState,
    FileReadState ProjectionReadState,
    bool CanWriteSource,
    bool CanWriteProjection,
    bool ProjectionOutOfDate,
    bool HasPendingFlush,
    int TotalCount,
    IReadOnlyList<BroadcastHistoryRecentItemSummary> Recent);

public sealed record ActiveBroadcastPlatformSummary(
    StreamingPlatform Platform,
    string? ChannelId,
    string? StreamId);

public sealed record ActiveBroadcastDiagnosticsSummary(
    FileReadState ReadState,
    bool CanWrite,
    bool HasDraft,
    Guid? BroadcastId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<ActiveBroadcastPlatformSummary> Platforms);
