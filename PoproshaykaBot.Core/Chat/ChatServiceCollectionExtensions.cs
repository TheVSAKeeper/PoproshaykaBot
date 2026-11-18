using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Hosting.Components;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Users.Avatars;

namespace PoproshaykaBot.Core.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddChatPipeline(this IServiceCollection services)
    {
        services.AddSingleton<ChatIngestionService>();
        services.AddKeyedSingleton<IHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<ChatIngestionService>());
        services.AddSingleton<ChatSender>();
        services.AddKeyedSingleton<IHostedComponent>(
            StreamingPlatform.Twitch,
            (sp, _) => sp.GetRequiredService<ChatSender>());
        services.AddKeyedSingleton<IHostedComponent, ChatDecorationsHostedComponent>(StreamingPlatform.Twitch);
        services.AddPlatformHost(StreamingPlatform.Twitch);
        services.AddSingleton<IPlatformConnectionAdapter, TwitchPlatformConnectionAdapter>();

        services.AddSingleton<ChatHistoryCacheStore>();
        services.AddSingleton<IChatHistoryPersistenceSink>(static sp => sp.GetRequiredService<ChatHistoryCacheStore>());
        services.AddSingleton<ChatHistoryManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppLifetimeComponent, ChatHistoryCacheLifecycle>());
        services.AddSingleton<ChatDisplayStore>();
        services.AddSingleton<ChatDecorationsProvider>();
        services.AddSingleton<UserRankService>();
        services.AddSingleton<UserPointsManagementService>();

        if (services.All(descriptor => descriptor.ServiceType != typeof(IUserAvatarCache)))
        {
            services.AddUserAvatars();
        }

        services.TryAddSingleton<PlatformChannelRegistry>();
        services.AddSingleton<IChannelProvider>(sp => sp.GetRequiredService<PlatformChannelRegistry>());

        services.AddSingleton<TwitchChatMessenger>();
        services.AddSingleton<IChatPlatform, TwitchChatPlatform>();
        services.AddSingleton<IChatPlatformRegistry, ChatPlatformRegistry>();
        services.AddSingleton<IBroadcastMessenger, BroadcastMessenger>();

        services.AddSingleton<AudienceTracker>();

        RegisterChatCommands(services);

        services.AddSingleton<CommandResponseTracker>();

        services.AddSingleton<ChatCommandProcessor>(sp =>
        {
            var commands = sp.GetServices<IChatCommand>().ToList();
            var commandSettingsStore = sp.GetRequiredService<CommandSettingsStore>();
            var usageRepository = sp.GetRequiredService<CommandUsageRepository>();
            var logger = sp.GetRequiredService<ILogger<ChatCommandProcessor>>();
            var platformRegistry = sp.GetRequiredService<IChatPlatformRegistry>();
            var processor = new ChatCommandProcessor(commands,
                commandSettingsStore,
                usageRepository,
                logger,
                platformRegistry: platformRegistry);
            processor.Register(new HelpCommand(processor.GetAvailableCommands, processor.Prefix));
            return processor;
        });

        services.AddSingleton<ChatPipelineHandler>();

        return services;
    }

    private static void RegisterChatCommands(IServiceCollection services)
    {
        services.AddSingleton<IChatCommand, HelloCommand>();
        services.AddSingleton<IChatCommand, DonateCommand>();
        services.AddSingleton<IChatCommand, HowManyMessagesCommand>();
        services.AddSingleton<IChatCommand, BotStatsCommand>();
        services.AddSingleton<IChatCommand, TopUsersCommand>();
        services.AddSingleton<IChatCommand, MyProfileCommand>();
        services.AddSingleton<IChatCommand, ActiveUsersCommand>();
        services.AddSingleton<IChatCommand, ByeCommand>();
        services.AddSingleton<IChatCommand, StreamInfoCommand>();
        services.AddSingleton<IChatCommand, StreamsSummaryCommand>();
        services.AddSingleton<IChatCommand, TrumpCommand>();
        services.AddSingleton<IChatCommand, RanksCommand>();
        services.AddSingleton<IChatCommand, RankCommand>();
        services.AddSingleton<IChatCommand, ProfileCommand>();
        services.AddSingleton<IChatCommand, TitleCommand>();
        services.AddSingleton<IChatCommand, GameCommand>();
        services.AddSingleton<IChatCommand, LastStreamCommand>();
        services.AddSingleton<IChatCommand, StreamsCountCommand>();
        services.AddSingleton<IChatCommand, PeakStreamCommand>();
        services.AddSingleton<IChatCommand, WasThereCommand>();
        services.AddSingleton<IChatCommand, TopStreamsCommand>();
        services.AddSingleton<IChatCommand, LinkAccountCommand>();
        services.AddSingleton<IChatCommand, UnlinkAccountCommand>();
    }
}
