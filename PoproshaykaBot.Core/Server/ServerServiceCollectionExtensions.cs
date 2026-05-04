using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Server;

public static class ServerServiceCollectionExtensions
{
    public static IServiceCollection AddHttpServer(this IServiceCollection services)
    {
        services.AddSingleton<SseService>();
        services.AddSingleton<KestrelHttpServer>();
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<IAppLifetimeComponent, KestrelHttpServerLifetimeAdapter>();
        return services;
    }
}
