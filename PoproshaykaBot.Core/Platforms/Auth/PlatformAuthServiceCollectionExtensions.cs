using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PoproshaykaBot.Core.Platforms.Auth;

public static class PlatformAuthServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformAuthRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPlatformAuthRegistry, PlatformAuthRegistry>();

        return services;
    }
}
