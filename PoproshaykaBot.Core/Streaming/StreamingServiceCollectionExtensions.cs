using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.EventSub;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamingServiceCollectionExtensions
{
    public static IServiceCollection AddStreamMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<StreamMonitoringHost>();

        services.AddKeyedSingleton(TwitchEndpoints.EventSubBotSession, (sp, _) => CreateHost(sp, TwitchOAuthRole.Bot, TwitchEndpoints.EventSubBotSession));
        services.AddKeyedSingleton(TwitchEndpoints.EventSubBroadcasterSession, (sp, _) => CreateHost(sp, TwitchOAuthRole.Broadcaster, TwitchEndpoints.EventSubBroadcasterSession));
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredKeyedService<EventSubConnectionHost>(TwitchEndpoints.EventSubBotSession));
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredKeyedService<EventSubConnectionHost>(TwitchEndpoints.EventSubBroadcasterSession));

        services.AddSingleton<StreamStatusManager>();
        services.AddSingleton<IStreamStatus>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<StreamStatusWatchdog>();
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<StreamStatusWatchdog>());
        services.AddSingleton<ChannelUpdateSubscriber>();
        services.AddSingleton<IStreamHostedComponent>(sp => sp.GetRequiredService<ChannelUpdateSubscriber>());
        return services;
    }

    private static EventSubConnectionHost CreateHost(IServiceProvider sp, TwitchOAuthRole role, string clientKey)
    {
        return new(role,
            sp.GetRequiredKeyedService<ITwitchEventSubClient>(clientKey),
            sp.GetRequiredService<AccountsStore>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ILogger<EventSubConnectionHost>>());
    }
}
