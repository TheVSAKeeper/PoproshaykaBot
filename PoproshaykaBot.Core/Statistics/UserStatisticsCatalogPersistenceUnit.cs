using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class UserStatisticsCatalogPersistenceUnit(
    UserStatisticsRepository repository,
    StatisticsFileStore fileStore,
    LegacyUserStatisticsMigrator migrator,
    ILogger<UserStatisticsCatalogPersistenceUnit> logger)
    : SingleFileStatisticsUnit<UserStatisticsCatalogDocument>(
        LegacyDataCatalog.UserStatisticsCatalogFileName,
        "каталог статистики пользователей",
        logger,
        [
            LegacyDataCatalog.UserStatisticsCatalogFileName,
            LegacyDataCatalog.UserStatisticsFileName,
            $"{LegacyDataCatalog.YouTubeFolderName}/{LegacyDataCatalog.UserStatisticsFileName}",
        ])
{
    protected override bool HasChanges => repository.HasChanges;

    protected override async Task<StatisticsReadResult<UserStatisticsCatalogDocument>> ReadAsync(CancellationToken cancellationToken)
    {
        var catalogResult = await fileStore.LoadUserStatisticsCatalogAsync(cancellationToken).ConfigureAwait(false);

        if (catalogResult.Failed)
        {
            return StatisticsReadResult<UserStatisticsCatalogDocument>.Failure;
        }

        if (catalogResult.Value is not null)
        {
            return catalogResult;
        }

        var migration = await migrator.TryMigrateAsync(cancellationToken).ConfigureAwait(false);
        return migration.Status switch
        {
            LegacyMigrationStatus.Failed => StatisticsReadResult<UserStatisticsCatalogDocument>.Failure,
            LegacyMigrationStatus.Migrated => StatisticsReadResult<UserStatisticsCatalogDocument>.FromFile(migration.Document!),
            _ => StatisticsReadResult<UserStatisticsCatalogDocument>.NoFile,
        };
    }

    protected override void ApplyLoaded(UserStatisticsCatalogDocument? data)
    {
        repository.ReplaceCatalog(data ?? new UserStatisticsCatalogDocument
        {
            SchemaVersion = UserStatisticsCatalogDocument.CurrentSchemaVersion,
            Migration = new()
            {
                Version = UserStatisticsCatalogDocument.CurrentMigrationVersion,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                MigratedPlatforms = [],
            },
        });
    }

    protected override UserStatisticsCatalogDocument CreateSnapshotAndMarkSaved()
    {
        return repository.CreateSnapshotAndMarkSaved();
    }

    protected override void MarkChanged()
    {
        repository.MarkChanged();
    }

    protected override Task WriteAsync(UserStatisticsCatalogDocument snapshot, CancellationToken cancellationToken)
    {
        return fileStore.SaveUserStatisticsCatalogAsync(snapshot, cancellationToken);
    }

    protected override void OnAfterLoaded(UserStatisticsCatalogDocument? data)
    {
        logger.LogInformation(
            "Каталог статистики пользователей загружен. Аккаунтов: {AccountCount}, канальных счётчиков: {ChannelCounters}, без канала: {UnattributedCounters}, связей: {LinkGroups}",
            data?.Accounts.Count ?? 0,
            data?.ChannelCounters.Count ?? 0,
            data?.UnattributedCounters.Count ?? 0,
            data?.AccountLinks.Count ?? 0);
    }
}
