using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public enum BroadcastProfileSyncState
{
    None = 0,
    Synced = 1,
    Drifted = 2,
    Pending = 3,
}

public sealed record BroadcastProfileSyncEvaluation(
    StreamingPlatform Platform,
    BroadcastProfileSyncState State,
    string? Reason = null);

public sealed record BroadcastProfileSyncSummary(
    BroadcastProfileSyncState State,
    IReadOnlyList<StreamingPlatform> DriftedPlatforms,
    IReadOnlyList<StreamingPlatform> PendingPlatforms)
{
    public static BroadcastProfileSyncSummary Empty { get; } =
        new(BroadcastProfileSyncState.None, [], []);
}
