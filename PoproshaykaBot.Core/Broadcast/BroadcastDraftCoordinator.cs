using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class BroadcastDraftCoordinator(
    BroadcastHistoryStore historyStore,
    ActiveBroadcastStore activeBroadcastStore,
    ITargetChannelProvider targetChannelProvider,
    TimeProvider timeProvider,
    ILogger<BroadcastDraftCoordinator> logger) : IBroadcastDraftCoordinator
{
    public void RecoverCompletedDraftOnStartup()
    {
        if (!targetChannelProvider.Current.RecordsUserData)
        {
            return;
        }

        var draft = activeBroadcastStore.Load();

        if (draft is null || draft.EndedAt is null)
        {
            return;
        }

        try
        {
            var record = ActiveBroadcastMapper.ToBroadcastRecord(draft);

            if (historyStore.TryAppend(record) || historyStore.IsBroadcastPersistedOnDisk(record.Id))
            {
                activeBroadcastStore.TryDelete(record.Id);
                logger.LogInformation(
                    "BroadcastDraftCoordinator: завершённый черновик эфира {BroadcastId} донесён в историю и очищен",
                    record.Id);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "BroadcastDraftCoordinator: ошибка восстановления завершённого черновика {BroadcastId}",
                draft.BroadcastId);
        }
    }

    public Guid ResolveBroadcastIdOnStart(
        StreamingPlatform platform,
        string channel,
        StreamInfo? stream,
        DateTimeOffset startedAt)
    {
        if (!targetChannelProvider.Current.RecordsUserData)
        {
            return Guid.NewGuid();
        }

        var draft = activeBroadcastStore.Load();

        if (draft is null || draft.EndedAt is not null || draft.BroadcastId == Guid.Empty)
        {
            return Guid.NewGuid();
        }

        var streamId = string.IsNullOrEmpty(stream?.Id) ? null : stream.Id;
        var channelId = string.IsNullOrWhiteSpace(stream?.UserId) ? null : stream.UserId;

        foreach (var candidate in draft.Platforms)
        {
            if (IsSamePlatformStream(candidate, platform, channel, streamId, startedAt, channelId))
            {
                return draft.BroadcastId;
            }
        }

        return Guid.NewGuid();
    }

    public BroadcastRecord FinalizeCompletedDraft(ActiveBroadcast completedDraft)
    {
        ArgumentNullException.ThrowIfNull(completedDraft);

        var now = timeProvider.GetUtcNow();
        var endedAt = completedDraft.EndedAt
                      ?? (completedDraft.UpdatedAt != default ? completedDraft.UpdatedAt : now);
        completedDraft.EndedAt = endedAt;

        foreach (var platform in completedDraft.Platforms)
        {
            platform.EndedAt ??= endedAt;
        }

        var finalDraftSaved = false;
        if (targetChannelProvider.Current.RecordsUserData)
        {
            try
            {
                finalDraftSaved = activeBroadcastStore.TrySave(completedDraft);
                if (!finalDraftSaved)
                {
                    logger.LogError("BroadcastDraftCoordinator: завершённый черновик {BroadcastId} не сохранён, пытаюсь записать эфир непосредственно в историю",
                        completedDraft.BroadcastId);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "BroadcastDraftCoordinator: ошибка сохранения завершённого черновика {BroadcastId}",
                    completedDraft.BroadcastId);
            }
        }

        var record = ActiveBroadcastMapper.ToBroadcastRecord(completedDraft, endedAt);

        if (!targetChannelProvider.Current.RecordsUserData)
        {
            return record;
        }

        try
        {
            var persisted = historyStore.TryAppend(record);

            if (persisted || historyStore.IsBroadcastPersistedOnDisk(record.Id))
            {
                activeBroadcastStore.TryDelete(record.Id);
            }
            else if (!finalDraftSaved)
            {
                logger.LogCritical("BroadcastDraftCoordinator: эфир {BroadcastId} остался только в памяти, запись черновика и истории не удалась",
                    record.Id);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "BroadcastDraftCoordinator: ошибка финализации эфира {BroadcastId}",
                record.Id);
        }

        return record;
    }

    public BroadcastRecord? TryFinalizeOrphanDraft(StreamingPlatform platform, DateTimeOffset? endedAt = null)
    {
        if (!targetChannelProvider.Current.RecordsUserData)
        {
            return null;
        }

        var draft = activeBroadcastStore.Load();

        if (draft is null)
        {
            return null;
        }

        if (draft.EndedAt is not null)
        {
            RecoverCompletedDraftOnStartup();
            return null;
        }

        var matching = draft.Platforms.FirstOrDefault(
            p => p.Platform == platform && p.StartedAt != default && !string.IsNullOrEmpty(p.Channel));

        if (matching is null)
        {
            var hasAnyUsable = draft.Platforms.Any(static p => p.StartedAt != default && !string.IsNullOrEmpty(p.Channel));
            if (!hasAnyUsable)
            {
                activeBroadcastStore.TryDelete();
            }

            return null;
        }

        var lastAlive = matching.UpdatedAt > matching.StartedAt ? matching.UpdatedAt : matching.StartedAt;
        if (matching.TrackedIntervals is not null)
        {
            foreach (var interval in matching.TrackedIntervals)
            {
                interval.EndedAt ??= lastAlive > interval.StartedAt ? lastAlive : interval.StartedAt;
            }
        }

        var effectiveEnd = endedAt is { } confirmedEnd && confirmedEnd > lastAlive ? confirmedEnd : lastAlive;
        matching.EndedAt ??= effectiveEnd;
        draft.UpdatedAt = effectiveEnd > draft.UpdatedAt ? effectiveEnd : draft.UpdatedAt;

        var allEnded = draft.Platforms.All(static p => p.EndedAt is not null);
        if (!allEnded)
        {
            activeBroadcastStore.TrySave(draft);
            return null;
        }

        draft.EndedAt = draft.Platforms.Max(static p => p.EndedAt) ?? effectiveEnd;
        return FinalizeCompletedDraft(draft);
    }

    internal static bool IsSamePlatformStream(
        ActiveBroadcastPlatform candidate,
        StreamingPlatform platform,
        string channel,
        string? streamId,
        DateTimeOffset startedAt,
        string? channelId = null)
    {
        if (candidate.Platform != platform || candidate.StartedAt == default || string.IsNullOrEmpty(candidate.Channel))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ChannelId) && !string.IsNullOrWhiteSpace(channelId))
        {
            if (!string.Equals(candidate.ChannelId, channelId, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (!string.Equals(candidate.Channel, channel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(candidate.StreamId))
        {
            return string.Equals(candidate.StreamId, streamId, StringComparison.Ordinal);
        }

        return (candidate.StartedAt - startedAt).Duration() <= StreamSessionStatisticsHandler.StreamMatchTolerance;
    }
}
