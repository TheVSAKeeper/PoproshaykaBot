using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Obs;

public static class ObsServiceCollectionExtensions
{
    public static IServiceCollection AddObsIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IObsWebSocketClient, ObsWebSocketClient>();
        services.AddSingleton<ObsIntegrationService>();
        services.AddSingleton<IAppLifetimeComponent, ObsIntegrationLifetimeAdapter>();

        return services;
    }
}
