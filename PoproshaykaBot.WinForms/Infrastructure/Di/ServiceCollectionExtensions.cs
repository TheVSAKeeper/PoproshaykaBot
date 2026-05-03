using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Infrastructure.Hosting.Components;
using PoproshaykaBot.WinForms.Infrastructure.Logging;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Server;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Stores;
using PoproshaykaBot.WinForms.Statistics;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Tiles;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Chat;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using PoproshaykaBot.WinForms.Users;
using Serilog;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, UiLogSink uiLogSink)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddSingleton(uiLogSink);
        services.AddHttpClient();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryEventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        services.AddSingleton<UiEventDispatcher>();
        services.AddSingleton<AppHost>();

        services.AddSingleton<IControlFactory, ControlFactory>();
        services.AddSingleton<IFormFactory, FormFactory>();

        services.AddSingleton<StatisticsCollector>();
        services.AddSingleton<IHostedComponent, StatisticsHostedComponent>();
        services.AddSingleton<IHostedComponent, ChatDecorationsHostedComponent>();
        services.AddSingleton<IHostedComponent, BroadcastSchedulerHostedComponent>();

        services.AddEventSubscribers(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }

    public static IServiceCollection AddSettingsStores(this IServiceCollection services)
    {
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<AccountsStore>();
        services.AddSingleton<BroadcastProfilesStore>();
        services.AddSingleton<PollsStore>();
        services.AddSingleton<RecentCategoriesStore>();
        services.AddSingleton<DashboardLayoutStore>();
        services.AddSingleton<ObsChatStore>();
        return services;
    }

    public static IServiceCollection AddTwitchClients(this IServiceCollection services)
    {
        services.AddTransient<BotTwitchAuthHandler>();
        services.AddTransient<BroadcasterTwitchAuthHandler>();

        services.AddHttpClient(TwitchEndpoints.HelixBotClient, client =>
            {
                client.BaseAddress = new(TwitchEndpoints.HelixBaseUrl);
            })
            .AddHttpMessageHandler<BotTwitchAuthHandler>();

        services.AddHttpClient(TwitchEndpoints.HelixBroadcasterClient, client =>
            {
                client.BaseAddress = new(TwitchEndpoints.HelixBaseUrl);
            })
            .AddHttpMessageHandler<BroadcasterTwitchAuthHandler>();

        services.AddKeyedSingleton<ITwitchHelixClient, BotHelixClient>(TwitchEndpoints.HelixBotClient);
        services.AddKeyedSingleton<ITwitchHelixClient, BroadcasterHelixClient>(TwitchEndpoints.HelixBroadcasterClient);

        services.AddSingleton<ITwitchEventSubClient, TwitchEventSubClient>();
        services.AddSingleton<TwitchOAuthService>();

        return services;
    }

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

    public static IServiceCollection AddPolls(this IServiceCollection services)
    {
        services.AddSingleton<PollProfilesManager>();
        services.AddSingleton<PollSnapshotStore>();
        services.AddSingleton<PollsAvailabilityService>();
        services.AddSingleton<PollController>();
        services.AddSingleton<IPollController>(sp => sp.GetRequiredService<PollController>());
        services.AddSingleton<PollEventSubscriber>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollEventSubscriber>());
        services.AddSingleton<PollHistoryStore>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollHistoryStore>());
        return services;
    }

    public static IServiceCollection AddHttpServer(this IServiceCollection services)
    {
        services.AddSingleton<SseService>();
        services.AddSingleton<KestrelHttpServer>();
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<IAppLifetimeComponent, KestrelHttpServerLifetimeAdapter>();
        return services;
    }

    public static IServiceCollection AddDashboardTiles(this IServiceCollection services)
    {
        services.AddSingleton<DashboardTileType, StreamInfoTileType>();
        services.AddSingleton<DashboardTileType, BroadcastStatusTileType>();
        services.AddSingleton<DashboardTileType, LogsTileType>();
        services.AddSingleton<DashboardTileType, TwitchChatTileType>();
        services.AddSingleton<DashboardTileType, ChatOverlayPreviewTileType>();
        services.AddSingleton<DashboardTileType, BroadcastProfilesTileType>();
        services.AddSingleton<DashboardTileType, PollsControlTileType>();
        services.AddSingleton<IDashboardTileCatalog, DashboardTileCatalog>();
        return services;
    }

    public static IServiceCollection AddForms(this IServiceCollection services)
    {
        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<MainForm>();

        services.AddTransient<SettingsForm>();
        services.AddTransient<UserStatisticsForm>();
        services.AddTransient<BroadcastProfileEditDialog>();
        services.AddTransient<PollFromProfileDialog>();
        services.AddTransient<PollProfileEditDialog>();
        return services;
    }
}
