using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Update;

public static class UpdateServiceCollectionExtensions
{
    public static IServiceCollection AddSelfUpdate(this IServiceCollection services)
    {
        services.AddHttpClient(GitHubReleaseClient.HttpClientName, client =>
        {
            client.BaseAddress = new("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PoproshaykaBot");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });

        services.AddSingleton<IUpdateEnvironment, UpdateEnvironment>();
        services.AddSingleton<IUpdateRepositoryProvider, UpdateRepositoryProvider>();
        services.AddSingleton<IGitHubReleaseClient, GitHubReleaseClient>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
        services.AddSingleton<IUpdateCoordinator, UpdateCoordinator>();
        services.AddSingleton<IAppLifetimeComponent, UpdateBackgroundService>();

        return services;
    }
}
