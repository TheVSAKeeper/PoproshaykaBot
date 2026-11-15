using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.YouTube;

public static class YouTubeServiceCollectionExtensions
{
    public static IServiceCollection AddYouTubeClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(YouTubeOAuthOptions.HttpClientName);
        services.TryAddSingleton(static _ => new YouTubeOAuthOptions());
        services.AddSingleton<YouTubeOAuthStatusReporter>();
        services.AddSingleton<YouTubeOAuthTokenClient>();
        services.AddSingleton<YouTubeOAuthAccountWriter>();
        services.AddSingleton<YouTubeOAuthFlowCoordinator>();
        services.AddSingleton<YouTubeOAuthTokenRefresher>();
        services.AddSingleton<YouTubeOAuthService>();
        services.AddSingleton<IYouTubeOAuthService>(static provider => provider.GetRequiredService<YouTubeOAuthService>());

        return services;
    }
}
