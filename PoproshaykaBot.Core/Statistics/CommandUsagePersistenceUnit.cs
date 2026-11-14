using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class CommandUsagePersistenceUnit(
    CommandUsageRepository repository,
    StatisticsFileStore fileStore,
    ILogger<CommandUsagePersistenceUnit> logger)
    : SingleFileStatisticsUnit<CommandUsageData>(
        StatisticsFileStore.CommandUsageFileName,
        "команд",
        logger)
{
    protected override bool HasChanges => repository.HasChanges;

    protected override bool ReloadWhenAlreadyLoadedOnStart => true;

    protected override Task<StatisticsReadResult<CommandUsageData>> ReadAsync(CancellationToken cancellationToken)
    {
        return fileStore.LoadCommandUsageAsync(cancellationToken);
    }

    protected override void ApplyLoaded(CommandUsageData? data)
    {
        repository.Replace(data ?? new CommandUsageData());
    }

    protected override CommandUsageData CreateSnapshotAndMarkSaved()
    {
        return repository.CreateSnapshotAndMarkSaved();
    }

    protected override void MarkChanged()
    {
        repository.MarkChanged();
    }

    protected override Task WriteAsync(CommandUsageData snapshot, CancellationToken cancellationToken)
    {
        return fileStore.SaveCommandUsageAsync(snapshot, cancellationToken);
    }

    protected override void OnAfterLoaded(CommandUsageData? data)
    {
        logger.LogInformation("Статистика команд загружена");
    }
}
