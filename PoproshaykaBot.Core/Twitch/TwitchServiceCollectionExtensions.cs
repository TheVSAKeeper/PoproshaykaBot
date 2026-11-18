using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.Twitch.Onboarding;

namespace PoproshaykaBot.Core.Twitch;

public static class TwitchServiceCollectionExtensions
{
    public static IServiceCollection AddTwitchClients(this IServiceCollection services)
    {
        services.AddPlatformAuthRegistry();
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

        services.AddHttpClient(TwitchEndpoints.HelixAppClient, client =>
        {
            client.BaseAddress = new(TwitchEndpoints.HelixBaseUrl);
        });

        services.AddKeyedSingleton<ITwitchHelixClient, BotHelixClient>(TwitchEndpoints.HelixBotClient);
        services.AddKeyedSingleton<ITwitchHelixClient, BroadcasterHelixClient>(TwitchEndpoints.HelixBroadcasterClient);

        services.AddKeyedSingleton<ITwitchEventSubClient, TwitchEventSubClient>(TwitchEndpoints.EventSubBotSession);
        services.AddKeyedSingleton<ITwitchEventSubClient, TwitchEventSubClient>(TwitchEndpoints.EventSubBroadcasterSession);

        services.AddSingleton<OAuthStatusReporter>();
        services.AddSingleton<OAuthTokenClient>();
        services.AddSingleton<OAuthAccountWriter>();
        services.AddSingleton<OAuthFlowCoordinator>();
        services.AddSingleton<OAuthTokenRefresher>();
        services.AddSingleton<TwitchOAuthService>();
        services.AddSingleton<ITwitchOAuthService>(static provider => provider.GetRequiredService<TwitchOAuthService>());
        services.AddSingleton<IPlatformAuthService>(static provider => provider.GetRequiredService<TwitchOAuthService>());
        services.AddKeyedSingleton<IPlatformAuthService>(
            StreamingPlatform.Twitch,
            static (provider, _) => provider.GetRequiredService<TwitchOAuthService>());
        services.AddSingleton<IClientCredentialsValidator, ClientCredentialsValidator>();

        services.AddSingleton<IBroadcasterIdProvider, BroadcasterIdProvider>();
        services.AddSingleton<IOnboardingChannelValidator, OnboardingChannelValidator>();
        services.AddSingleton<IPlatformChannelAdapter, TwitchChannelAdapter>();

        services.AddSingleton<ISettingsSnapshotContributor>(static provider => new DelegateSettingsSnapshotContributor(() =>
        {
            var accounts = provider.GetRequiredService<AccountsStore>();
            return
            [
                new("Account.Bot", AccountsStore.Describe(accounts.Load(PlatformAuthRole.Bot))),
                new("Account.Broadcaster", AccountsStore.Describe(accounts.Load(PlatformAuthRole.Broadcaster))),
            ];
        }));

        foreach (var role in PlatformAuthRoles.Active)
        {
            services.AddSingleton<ISecretValueProvider>(provider =>
                new DelegateSecretValueProvider($"токены роли {role}", () =>
                {
                    var accounts = provider.GetRequiredService<AccountsStore>().GetAllAccounts(role);
                    return accounts.SelectMany(static account => new[] { account.AccessToken, account.RefreshToken }).ToArray();
                }));
        }

        return services;
    }
}
