using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Forms.Broadcast;
using PoproshaykaBot.WinForms.Forms.Polls;
using PoproshaykaBot.WinForms.Forms.Settings;
using PoproshaykaBot.WinForms.Forms.Users;
using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class ServiceCollectionExtensions
{
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
        services.AddSingleton<IControlFactory, ControlFactory>();
        services.AddSingleton<IFormFactory, FormFactory>();

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
