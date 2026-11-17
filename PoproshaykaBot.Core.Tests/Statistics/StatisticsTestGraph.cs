using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

internal sealed class StatisticsTestGraph
{
    public StatisticsTestGraph(
        string directory,
        UserStatisticsRepository? userRepository = null,
        IBotStatisticsRepository? botRepository = null,
        CommandUsageRepository? commandUsageRepository = null,
        ILogger<StatisticsAutoSaver>? saverLogger = null,
        TimeSpan? autoSaveInterval = null,
        BroadcastHistoryStore? broadcastHistoryStore = null,
        ActiveBroadcastStore? activeBroadcastStore = null,
        KnownChannelIdentityResolver? identityResolver = null,
        params IExternalWriteParticipant[] extraParticipants)
    {
        UserRepository = userRepository ?? new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        BotRepository = botRepository ?? new BotStatisticsRepository();
        CommandUsageRepository = commandUsageRepository ?? new CommandUsageRepository(TimeProvider.System);
        FileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, directory);
        var migrator = new LegacyUserStatisticsMigrator(
            FileStore,
            identityResolver ?? new KnownChannelIdentityResolver(),
            NullLogger<LegacyUserStatisticsMigrator>.Instance,
            broadcastHistoryStore,
            activeBroadcastStore);

        UserUnit = new UserStatisticsCatalogPersistenceUnit(
            UserRepository,
            FileStore,
            migrator,
            NullLogger<UserStatisticsCatalogPersistenceUnit>.Instance);

        UserLoader = new UserStatisticsLoader(UserUnit);

        var botUnit = new BotStatisticsPersistenceUnit(
            BotRepository,
            FileStore,
            NullLogger<BotStatisticsPersistenceUnit>.Instance);

        var commandUsageUnit = new CommandUsagePersistenceUnit(
            CommandUsageRepository,
            FileStore,
            NullLogger<CommandUsagePersistenceUnit>.Instance);

        IPeriodicStatisticsUnit[] periodicUnits = [UserUnit, botUnit, commandUsageUnit];
        IExternalWriteParticipant[] participants = [UserUnit, botUnit, commandUsageUnit, .. extraParticipants];
        IFlushableHistoryStore[] flushableStores = [.. extraParticipants.OfType<IFlushableHistoryStore>()];

        var logger = saverLogger ?? NullLogger<StatisticsAutoSaver>.Instance;
        AutoSaver = autoSaveInterval.HasValue
            ? new StatisticsAutoSaver(periodicUnits, participants, flushableStores, logger, autoSaveInterval.Value)
            : new StatisticsAutoSaver(periodicUnits, participants, flushableStores, logger);
    }

    public UserStatisticsRepository UserRepository { get; }

    public IBotStatisticsRepository BotRepository { get; }

    public CommandUsageRepository CommandUsageRepository { get; }

    public StatisticsFileStore FileStore { get; }

    internal UserStatisticsCatalogPersistenceUnit UserUnit { get; }

    public UserStatisticsLoader UserLoader { get; }

    public StatisticsAutoSaver AutoSaver { get; }
}
