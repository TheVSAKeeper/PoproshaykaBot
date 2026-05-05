using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Server.Endpoints;

namespace PoproshaykaBot.Core.Server;

public static class ServerServiceCollectionExtensions
{
    public static IServiceCollection AddHttpServer(this IServiceCollection services)
    {
        services.AddSingleton<SseChannelOptions>();
        services.AddSingleton<SseDropMetrics>();
        services.AddSingleton<SseClientRegistry>();
        services.AddSingleton<SseService>();
        services.AddSingleton<KestrelHttpServer>();
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<IAppLifetimeComponent, KestrelHttpServerLifetimeAdapter>();

        services.AddSingleton<IEndpointMapper, OAuthCallbackEndpoint>();
        services.AddSingleton<IEndpointMapper, SseEndpoint>();
        services.AddSingleton<IEndpointMapper, ChatHistoryEndpoint>();
        services.AddSingleton<IEndpointMapper, ChatSettingsEndpoint>();
        services.AddSingleton<IEndpointMapper, AvatarEndpoint>();
        services.AddSingleton<IEndpointMapper, StaticAssetsEndpoint>();

        return services;
    }
}
