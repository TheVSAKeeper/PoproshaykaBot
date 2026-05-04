using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Twitch.EventSub;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamingServiceCollectionExtensions
{
    public static IServiceCollection AddStreamMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<StreamMonitoringHost>();
        services.AddSingleton<EventSubConnectionHost>();
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<EventSubConnectionHost>());
        services.AddSingleton<StreamStatusManager>();
        services.AddSingleton<IStreamStatus>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<StreamStatusWatchdog>();
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<StreamStatusWatchdog>());
        services.AddSingleton<ChannelUpdateSubscriber>();
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<ChannelUpdateSubscriber>());
        return services;
    }
}
