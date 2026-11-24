using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics.Linking;

namespace PoproshaykaBot.Core.Statistics;

public static class StatisticsServiceCollectionExtensions
{
    public static IServiceCollection AddStatistics(this IServiceCollection services)
    {
        services.AddSingleton<UserStatisticsRepository>();
        services.AddSingleton<IUserStatisticsRepository>(sp => sp.GetRequiredService<UserStatisticsRepository>());
        services.AddSingleton<BotStatisticsRepository>();
        services.AddSingleton<IBotStatisticsRepository>(sp => sp.GetRequiredService<BotStatisticsRepository>());
        services.AddSingleton<CommandUsageRepository>();
        services.AddSingleton<StatisticsFileStore>();
        services.AddSingleton<BroadcastHistoryStore>();
        services.AddSingleton<ActiveBroadcastStore>();
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<BroadcastHistoryStore>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<ActiveBroadcastStore>());
        services.AddSingleton<IFlushableHistoryStore>(sp => sp.GetRequiredService<BroadcastHistoryStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppLifetimeComponent, BroadcastHistoryLifetimeComponent>());

        services.AddSingleton(sp => new KnownChannelIdentityResolver(
            sp.GetService<ChannelCatalogStore>(),
            sp.GetServices<IPlatformChannelIdentitySource>()));

        services.AddSingleton(sp => new LegacyUserStatisticsMigrator(
            sp.GetRequiredService<StatisticsFileStore>(),
            sp.GetRequiredService<KnownChannelIdentityResolver>(),
            sp.GetRequiredService<ILogger<LegacyUserStatisticsMigrator>>(),
            sp.GetService<BroadcastHistoryStore>(),
            sp.GetService<ActiveBroadcastStore>(),
            sp.GetService<TimeProvider>()));

        services.AddSingleton<UserStatisticsCatalogPersistenceUnit>();
        services.AddSingleton<IPeriodicStatisticsUnit>(sp => sp.GetRequiredService<UserStatisticsCatalogPersistenceUnit>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<UserStatisticsCatalogPersistenceUnit>());

        services.AddSingleton<BotStatisticsPersistenceUnit>();
        services.AddSingleton<IPeriodicStatisticsUnit>(sp => sp.GetRequiredService<BotStatisticsPersistenceUnit>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<BotStatisticsPersistenceUnit>());

        services.AddSingleton<CommandUsagePersistenceUnit>();
        services.AddSingleton<IPeriodicStatisticsUnit>(sp => sp.GetRequiredService<CommandUsagePersistenceUnit>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<CommandUsagePersistenceUnit>());

        services.AddSingleton(sp => new UserStatisticsLoader(sp.GetRequiredService<UserStatisticsCatalogPersistenceUnit>()));
        services.AddSingleton<IAccountLinkingService>(sp => new AccountLinkingService(
            sp.GetRequiredService<IUserStatisticsRepository>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetService<TimeProvider>()));
        services.AddSingleton<StatisticsAutoSaver>();
        services.AddSingleton(sp => new PointsTransferService(
            sp.GetRequiredService<IUserStatisticsRepository>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<StatisticsAutoSaver>(),
            sp.GetRequiredService<UserStatisticsLoader>()));
        services.AddKeyedSingleton<IHostedComponent>(
            HostingKeys.Shared,
            (sp, _) => sp.GetRequiredService<StatisticsAutoSaver>());
        return services;
    }
}
