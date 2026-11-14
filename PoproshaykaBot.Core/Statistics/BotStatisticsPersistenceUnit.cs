using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class BotStatisticsPersistenceUnit(
    IBotStatisticsRepository repository,
    StatisticsFileStore fileStore,
    ILogger<BotStatisticsPersistenceUnit> logger)
    : SingleFileStatisticsUnit<BotStatistics>(
        LegacyDataCatalog.BotStatisticsFileName,
        "бота",
        logger)
{
    protected override bool HasChanges => repository.HasChanges;

    protected override bool ReloadWhenAlreadyLoadedOnStart => true;

    public override void OnStarted()
    {
        repository.ResetStartTime();
    }

    protected override Task<StatisticsReadResult<BotStatistics>> ReadAsync(CancellationToken cancellationToken)
    {
        return fileStore.LoadBotAsync(cancellationToken);
    }

    protected override void ApplyLoaded(BotStatistics? data)
    {
        repository.Replace(data ?? BotStatistics.Create());
    }

    protected override BotStatistics CreateSnapshotAndMarkSaved()
    {
        return repository.CreateSnapshotAndMarkSaved();
    }

    protected override void MarkChanged()
    {
        repository.MarkChanged();
    }

    protected override Task WriteAsync(BotStatistics snapshot, CancellationToken cancellationToken)
    {
        return fileStore.SaveBotAsync(snapshot, cancellationToken);
    }

    protected override void OnAfterLoaded(BotStatistics? data)
    {
        logger.LogInformation("Статистика бота загружена");
    }
}
