using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class PlatformUserStatisticsUnit(
    UserStatisticsPlatformDescriptor descriptor,
    UserStatisticsPartition partition,
    StatisticsFileStore fileStore,
    ILogger<PlatformUserStatisticsUnit> logger)
    : SingleFileStatisticsUnit<List<UserStatistics>>(
        descriptor.RelativePath,
        $"{descriptor.EntityName} ({descriptor.Platform})",
        logger)
{
    public StreamingPlatform Platform => descriptor.Platform;

    protected override bool HasChanges => partition.HasChanges;

    protected override Task<StatisticsReadResult<List<UserStatistics>>> ReadAsync(CancellationToken cancellationToken)
    {
        return fileStore.LoadUsersAsync(descriptor.Platform, cancellationToken);
    }

    protected override void ApplyLoaded(List<UserStatistics>? data)
    {
        partition.Replace(data ?? []);
    }

    protected override List<UserStatistics> CreateSnapshotAndMarkSaved()
    {
        return partition.CreateSnapshotAndMarkSaved();
    }

    protected override void MarkChanged()
    {
        partition.MarkChanged();
    }

    protected override Task WriteAsync(List<UserStatistics> snapshot, CancellationToken cancellationToken)
    {
        return fileStore.SaveUsersAsync(descriptor.Platform, snapshot, cancellationToken);
    }

    protected override void OnAfterLoaded(List<UserStatistics>? data)
    {
        logger.LogInformation(
            "Статистика пользователей ({Platform}) загружена. Загружено пользователей: {UserCount}",
            descriptor.Platform,
            data?.Count ?? 0);
    }
}
