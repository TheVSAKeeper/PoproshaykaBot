using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddChatPipeline(this IServiceCollection services)
    {
        services.AddSingleton<EventSubChatMessageMapper>();
        services.AddSingleton<ChatIngestionService>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<ChatIngestionService>());
        services.AddSingleton<ChatSender>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<ChatSender>());

        services.AddSingleton<ChatHistoryManager>();
        services.AddSingleton<ChatDecorationsProvider>();
        services.AddSingleton<UserRankService>();
        services.AddSingleton<UserMessagesManagementService>();
        services.AddSingleton<UserProfileImageProvider>();

        services.AddSingleton<TwitchChatMessenger>();
        services.AddSingleton<IChatMessenger>(sp => sp.GetRequiredService<TwitchChatMessenger>());

        services.AddSingleton<AudienceTracker>();

        services.AddSingleton<IChatCommand, HelloCommand>();
        services.AddSingleton<IChatCommand, DonateCommand>();
        services.AddSingleton<IChatCommand, HowManyMessagesCommand>();
        services.AddSingleton<IChatCommand, BotStatsCommand>();
        services.AddSingleton<IChatCommand, TopUsersCommand>();
        services.AddSingleton<IChatCommand, MyProfileCommand>();
        services.AddSingleton<IChatCommand, ActiveUsersCommand>();
        services.AddSingleton<IChatCommand, ByeCommand>();
        services.AddSingleton<IChatCommand, StreamInfoCommand>();
        services.AddSingleton<IChatCommand, TrumpCommand>();
        services.AddSingleton<IChatCommand, RanksCommand>();
        services.AddSingleton<IChatCommand, RankCommand>();
        services.AddSingleton<IChatCommand, ProfileCommand>();
        services.AddSingleton<IChatCommand, TitleCommand>();
        services.AddSingleton<IChatCommand, GameCommand>();

        services.AddSingleton<ChatCommandProcessor>(sp =>
        {
            var commands = sp.GetServices<IChatCommand>().ToList();
            var processor = new ChatCommandProcessor(commands);
            processor.Register(new HelpCommand(processor.GetAllCommands));
            return processor;
        });

        services.AddSingleton<TwitchChatHandler>();
        services.AddSingleton<IChannelProvider>(sp => sp.GetRequiredService<TwitchChatHandler>());

        return services;
    }
}
