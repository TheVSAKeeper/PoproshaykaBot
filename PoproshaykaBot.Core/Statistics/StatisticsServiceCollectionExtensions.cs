using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;

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

        foreach (var descriptor in UserStatisticsPlatformCatalog.All)
        {
            var platformDescriptor = descriptor;
            services.AddKeyedSingleton(
                platformDescriptor.Platform,
                (sp, _) => new PlatformUserStatisticsUnit(
                    platformDescriptor,
                    sp.GetRequiredService<UserStatisticsRepository>().GetPartition(platformDescriptor.Platform),
                    sp.GetRequiredService<StatisticsFileStore>(),
                    sp.GetRequiredService<ILogger<PlatformUserStatisticsUnit>>()));

            services.AddSingleton(sp => sp.GetRequiredKeyedService<PlatformUserStatisticsUnit>(platformDescriptor.Platform));
            services.AddSingleton<IPeriodicStatisticsUnit>(
                sp => sp.GetRequiredKeyedService<PlatformUserStatisticsUnit>(platformDescriptor.Platform));
            services.AddSingleton<IExternalWriteParticipant>(
                sp => sp.GetRequiredKeyedService<PlatformUserStatisticsUnit>(platformDescriptor.Platform));
        }

        services.AddSingleton<BotStatisticsPersistenceUnit>();
        services.AddSingleton<IPeriodicStatisticsUnit>(sp => sp.GetRequiredService<BotStatisticsPersistenceUnit>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<BotStatisticsPersistenceUnit>());

        services.AddSingleton<CommandUsagePersistenceUnit>();
        services.AddSingleton<IPeriodicStatisticsUnit>(sp => sp.GetRequiredService<CommandUsagePersistenceUnit>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<CommandUsagePersistenceUnit>());

        services.AddSingleton(sp => new UserStatisticsLoader(sp.GetServices<PlatformUserStatisticsUnit>()));
        services.AddSingleton<StatisticsAutoSaver>();
        services.AddKeyedSingleton<IHostedComponent>(
            HostingKeys.Shared,
            (sp, _) => sp.GetRequiredService<StatisticsAutoSaver>());
        return services;
    }
}
