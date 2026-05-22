using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Server;

public static class ServerServiceCollectionExtensions
{
    public static IServiceCollection AddHttpServer(this IServiceCollection services)
    {
        services.AddSingleton<SseChannelOptions>(sp =>
        {
            var infrastructure = sp.GetRequiredService<SettingsManager>().Current.Twitch.Infrastructure;
            return new(infrastructure.SseGlobalChannelCapacity,
                infrastructure.SseClientChannelCapacity,
                infrastructure.SseDropLogThrottle,
                infrastructure.SseDropNotifyThreshold);
        });

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
        services.AddSingleton<IEndpointMapper, AnimationsEndpoint>();
        services.AddSingleton<IEndpointMapper, AvatarEndpoint>();
        services.AddSingleton<IEndpointMapper, StaticAssetsEndpoint>();

        return services;
    }
}
