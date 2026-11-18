using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Broadcast;

public static class BroadcastServiceCollectionExtensions
{
    public static IServiceCollection AddBroadcasting(this IServiceCollection services)
    {
        services.AddSingleton<ITwitchChannelsApi, TwitchChannelsApiAdapter>();
        services.AddSingleton<ITwitchSearchApi, TwitchSearchApiAdapter>();
        services.AddSingleton<IBotUserIdProvider, BotUserIdProvider>();
        services.AddSingleton<IChannelUpdateConfirmation>(sp => sp.GetRequiredService<ChannelUpdateConfirmationService>());
        services.AddSingleton<ChannelInformationApplier>();
        services.AddSingleton<IChannelInformationApplier>(sp => sp.GetRequiredService<ChannelInformationApplier>());
        services.AddSingleton<GameCategoryResolver>();
        services.AddSingleton<IGameCategoryResolver>(sp => sp.GetRequiredService<GameCategoryResolver>());
        services.AddSingleton<BroadcastProfilesManager>();
        services.AddSingleton<IBroadcastPlatformTargetsProvider, ProfileBroadcastPlatformTargetsProvider>();
        services.AddSingleton<BroadcastDraftCoordinator>();
        services.AddSingleton<IBroadcastDraftCoordinator>(sp => sp.GetRequiredService<BroadcastDraftCoordinator>());
        services.TryAddSingleton<BroadcastAggregator>();
        services.AddSingleton<IBroadcastState>(sp => sp.GetRequiredService<BroadcastAggregator>());
        services.AddSingleton<IBroadcastChannelLifecycle>(sp => sp.GetRequiredService<BroadcastAggregator>());
        services.AddSingleton<IBroadcastOfflineStopCoordinator>(sp => sp.GetRequiredService<BroadcastAggregator>());
        services.AddSingleton<BroadcastScheduler>();
        services.AddSingleton<IBroadcastScheduler>(sp => sp.GetRequiredService<BroadcastScheduler>());
        return services;
    }
}
