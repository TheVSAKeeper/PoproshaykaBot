using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.YouTube;

public static class YouTubeServiceCollectionExtensions
{
    public static IServiceCollection AddYouTubeClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPlatformAuthRegistry();
        services.AddHttpClient(YouTubeOAuthOptions.HttpClientName);
        services.TryAddSingleton(static _ => new YouTubeOAuthOptions());
        services.AddSingleton<YouTubeOAuthStatusReporter>();
        services.AddSingleton<YouTubeOAuthTokenClient>();
        services.AddSingleton<IYouTubeChannelIdentityClient, YouTubeChannelIdentityClient>();
        services.AddSingleton<YouTubeOAuthAccountWriter>();
        services.AddSingleton<YouTubeOAuthFlowCoordinator>();
        services.AddSingleton<YouTubeOAuthTokenRefresher>();
        services.AddSingleton<YouTubeOAuthService>();
        services.AddSingleton<IYouTubeOAuthService>(static provider => provider.GetRequiredService<YouTubeOAuthService>());
        services.AddSingleton<IPlatformAuthService>(static provider => provider.GetRequiredService<YouTubeOAuthService>());

        services.AddSingleton<ISettingsSnapshotContributor>(static provider => new DelegateSettingsSnapshotContributor(() =>
        {
            var youtubeSettings = provider.GetRequiredService<YouTubeSettingsStore>();
            var youtubeAccounts = provider.GetRequiredService<YouTubeAccountsStore>();
            return
            [
                new("YouTubeSettings", YouTubeSettingsStore.Describe(youtubeSettings.Load())),
                new("YouTubeAccounts", YouTubeAccountsStore.Describe(
                    youtubeAccounts.LoadClientSecret(),
                    youtubeAccounts.Load(PlatformAuthRole.Bot),
                    youtubeAccounts.Load(PlatformAuthRole.Broadcaster))),
            ];
        }));

        services.AddSingleton<ISecretValueProvider>(static provider =>
            new DelegateSecretValueProvider("настройки YouTube", () => [provider.GetRequiredService<YouTubeAccountsStore>().LoadClientSecret()]));

        foreach (var role in PlatformAuthRoles.Active)
        {
            services.AddSingleton<ISecretValueProvider>(provider =>
                new DelegateSecretValueProvider($"токены YouTube роли {role}", () =>
                {
                    var account = provider.GetRequiredService<YouTubeAccountsStore>().Load(role);
                    return [account.AccessToken, account.RefreshToken];
                }));
        }

        return services;
    }
}
