namespace PoproshaykaBot.Core.Statistics;

public readonly record struct BroadcastHistoryMergeResult(IReadOnlyList<BroadcastRecord> Broadcasts, int MergedPairs);
