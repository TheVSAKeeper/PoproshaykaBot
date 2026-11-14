using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public interface IBroadcastState
{
    bool IsAnyLive { get; }

    Guid? CurrentBroadcastId { get; }

    IReadOnlySet<StreamingPlatform> LivePlatforms { get; }

    bool IsPlatformLive(StreamingPlatform platform);
}

public interface IBroadcastOfflineStopCoordinator
{
    void ScheduleOfflineStop(StreamingPlatform platform, Func<CancellationToken, Task> stopAction);
}

public interface IBroadcastDraftCoordinator
{
    void RecoverCompletedDraftOnStartup();

    Guid ResolveBroadcastIdOnStart(
        StreamingPlatform platform,
        string channel,
        StreamInfo? stream,
        DateTimeOffset startedAt);

    BroadcastRecord FinalizeCompletedDraft(ActiveBroadcast completedDraft);

    BroadcastRecord? TryFinalizeOrphanDraft(StreamingPlatform platform, DateTimeOffset endedAt);
}
