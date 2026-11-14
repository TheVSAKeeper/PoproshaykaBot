using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public static class HostingServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformHost(this IServiceCollection services, StreamingPlatform platform)
    {
        services.AddKeyedSingleton(
            platform,
            (sp, _) => new PlatformHost(
                platform,
                sp.GetKeyedServices<IHostedComponent>(platform),
                sp.GetRequiredService<ILogger<PlatformHost>>()));

        services.AddSingleton(sp => sp.GetRequiredKeyedService<PlatformHost>(platform));
        return services;
    }

    public static IServiceCollection AddPlatformStreamMonitoringHost(this IServiceCollection services, StreamingPlatform platform)
    {
        services.AddKeyedSingleton(
            platform,
            (sp, _) => new StreamMonitoringHost(
                platform,
                sp.GetKeyedServices<IStreamHostedComponent>(platform),
                sp.GetRequiredService<ILogger<StreamMonitoringHost>>()));

        services.AddSingleton(sp => sp.GetRequiredKeyedService<StreamMonitoringHost>(platform));
        return services;
    }
}
