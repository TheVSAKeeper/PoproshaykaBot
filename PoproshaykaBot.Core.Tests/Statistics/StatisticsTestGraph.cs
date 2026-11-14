using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
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
        params IExternalWriteParticipant[] extraParticipants)
    {
        UserRepository = userRepository ?? new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        BotRepository = botRepository ?? new BotStatisticsRepository();
        CommandUsageRepository = commandUsageRepository ?? new CommandUsageRepository(TimeProvider.System);
        FileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, directory);

        UserUnits = UserStatisticsPlatformCatalog.All
            .Select(descriptor => new PlatformUserStatisticsUnit(
                descriptor,
                UserRepository.GetPartition(descriptor.Platform),
                FileStore,
                NullLogger<PlatformUserStatisticsUnit>.Instance))
            .ToArray();

        UserLoader = new UserStatisticsLoader(UserUnits);

        var botUnit = new BotStatisticsPersistenceUnit(
            BotRepository,
            FileStore,
            NullLogger<BotStatisticsPersistenceUnit>.Instance);

        var commandUsageUnit = new CommandUsagePersistenceUnit(
            CommandUsageRepository,
            FileStore,
            NullLogger<CommandUsagePersistenceUnit>.Instance);

        IPeriodicStatisticsUnit[] periodicUnits = [.. UserUnits, botUnit, commandUsageUnit];
        IExternalWriteParticipant[] participants = [.. UserUnits, botUnit, commandUsageUnit, .. extraParticipants];
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

    public IReadOnlyList<PlatformUserStatisticsUnit> UserUnits { get; }

    public UserStatisticsLoader UserLoader { get; }

    public StatisticsAutoSaver AutoSaver { get; }

    internal PlatformUserStatisticsUnit GetUserUnit(StreamingPlatform platform)
    {
        return UserUnits.Single(unit => unit.Platform == platform);
    }
}
