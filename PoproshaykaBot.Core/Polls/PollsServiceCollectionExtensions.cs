using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Polls;

public static class PollsServiceCollectionExtensions
{
    public static IServiceCollection AddPolls(this IServiceCollection services)
    {
        services.AddSingleton<PollProfilesManager>();
        services.AddSingleton<PollSnapshotStore>();
        services.AddSingleton<PollsAvailabilityService>();
        services.AddSingleton<PollController>();
        services.AddSingleton<IPollController>(sp => sp.GetRequiredService<PollController>());
        services.AddSingleton<PollEventSubscriber>();
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<PollEventSubscriber>());
        services.AddSingleton<PollHistoryStore>();
        services.AddKeyedSingleton<IHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<PollHistoryStore>());
        services.AddSingleton<IExternalWriteParticipant>(sp => sp.GetRequiredService<PollHistoryStore>());
        services.AddSingleton<IFlushableHistoryStore>(sp => sp.GetRequiredService<PollHistoryStore>());
        return services;
    }
}
