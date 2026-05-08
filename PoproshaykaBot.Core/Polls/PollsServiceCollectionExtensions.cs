using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;

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
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollEventSubscriber>());
        services.AddSingleton<PollHistoryStore>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollHistoryStore>());
        return services;
    }
}
