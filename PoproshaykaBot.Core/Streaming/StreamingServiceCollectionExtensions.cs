using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamingServiceCollectionExtensions
{
    public static IServiceCollection AddStreamMonitoring(this IServiceCollection services)
    {
        services.AddPlatformStreamMonitoringHost(StreamingPlatform.Twitch);

        services.AddKeyedSingleton(TwitchEndpoints.EventSubBotSession, (sp, _) => CreateHost(sp, PlatformAuthRole.Bot, TwitchEndpoints.EventSubBotSession));
        services.AddKeyedSingleton(TwitchEndpoints.EventSubBroadcasterSession, (sp, _) => CreateHost(sp, PlatformAuthRole.Broadcaster, TwitchEndpoints.EventSubBroadcasterSession));
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredKeyedService<EventSubConnectionHost>(TwitchEndpoints.EventSubBotSession));
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredKeyedService<EventSubConnectionHost>(TwitchEndpoints.EventSubBroadcasterSession));

        services.AddSingleton<StreamStatusManager>();
        services.AddSingleton<IStreamStatus>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddKeyedSingleton<IStreamStatus>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<StreamStatusManager>());
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<StreamStatusWatchdog>();
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<StreamStatusWatchdog>());
        services.AddSingleton<ChannelUpdateSubscriber>();
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<ChannelUpdateSubscriber>());
        services.AddSingleton<IBotLifecycleAutomationSettingsProvider, BotLifecycleAutomationSettingsProvider>();
        return services;
    }

    private static EventSubConnectionHost CreateHost(IServiceProvider sp, PlatformAuthRole role, string clientKey)
    {
        return new(role,
            sp.GetRequiredKeyedService<ITwitchEventSubClient>(clientKey),
            sp.GetRequiredService<AccountsStore>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ILogger<EventSubConnectionHost>>());
    }
}
