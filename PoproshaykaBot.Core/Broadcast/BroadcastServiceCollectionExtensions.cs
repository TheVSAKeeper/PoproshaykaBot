using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Broadcast;

public static class BroadcastServiceCollectionExtensions
{
    public static IServiceCollection AddBroadcasting(this IServiceCollection services)
    {
        services.AddSingleton<ITwitchChannelsApi, TwitchChannelsApiAdapter>();
        services.AddSingleton<ITwitchSearchApi, TwitchSearchApiAdapter>();
        services.AddSingleton<IBroadcasterIdProvider, BroadcasterIdProvider>();
        services.AddSingleton<IBotUserIdProvider, BotUserIdProvider>();
        services.AddSingleton<IChannelUpdateConfirmation>(sp => sp.GetRequiredService<ChannelUpdateConfirmationService>());
        services.AddSingleton<ChannelInformationApplier>();
        services.AddSingleton<IChannelInformationApplier>(sp => sp.GetRequiredService<ChannelInformationApplier>());
        services.AddSingleton<GameCategoryResolver>();
        services.AddSingleton<IGameCategoryResolver>(sp => sp.GetRequiredService<GameCategoryResolver>());
        services.AddSingleton<BroadcastProfilesManager>();
        services.AddSingleton<BroadcastScheduler>();
        services.AddSingleton<IBroadcastScheduler>(sp => sp.GetRequiredService<BroadcastScheduler>());
        return services;
    }
}
