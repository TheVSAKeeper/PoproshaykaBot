using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;

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
        services.AddKeyedSingleton<IPlatformAuthService>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeOAuthService>());

        services.AddTransient<BotYouTubeAuthHandler>();
        services.AddTransient<BroadcasterYouTubeAuthHandler>();

        services.AddHttpClient(YouTubeEndpoints.BotApiHttpClient, static client =>
        {
            client.BaseAddress = YouTubeEndpoints.RestApiBaseUri;
        }).AddHttpMessageHandler<BotYouTubeAuthHandler>();

        services.AddHttpClient(YouTubeEndpoints.BroadcasterApiHttpClient, static client =>
        {
            client.BaseAddress = YouTubeEndpoints.RestApiBaseUri;
        }).AddHttpMessageHandler<BroadcasterYouTubeAuthHandler>();

        services.AddSingleton<YouTubeGrpcChannelProvider>();

        services.AddSingleton<YouTubeQuotaStore>();
        services.AddSingleton<IExternalWriteParticipant>(
            static provider => provider.GetRequiredService<YouTubeQuotaStore>());
        services.AddSingleton<IFlushableHistoryStore>(
            static provider => provider.GetRequiredService<YouTubeQuotaStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppLifetimeComponent, YouTubeQuotaLifetimeComponent>());
        services.AddSingleton<IYouTubeQuotaGovernor, YouTubeQuotaGovernor>();

        services.AddSingleton<YouTubeTargetChannelResolver>();
        services.AddSingleton<IYouTubeLiveBroadcastClient, YouTubeLiveBroadcastClient>();
        services.TryAddSingleton<IYouTubeBroadcastSelectionStore, InMemoryYouTubeBroadcastSelectionStore>();
        services.AddSingleton<YouTubeStreamMonitor>();
        services.AddSingleton<IYouTubeActiveBroadcastSource>(
            static provider => provider.GetRequiredService<YouTubeStreamMonitor>());
        services.AddKeyedSingleton<IStreamStatus>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeStreamMonitor>());
        services.AddKeyedSingleton<IStreamHostedComponent>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeStreamMonitor>());
        services.AddSingleton<IYouTubeMonitorDiagnostics>(
            static provider => provider.GetRequiredService<YouTubeStreamMonitor>());
        services.AddPlatformStreamMonitoringHost(StreamingPlatform.YouTube);

        services.AddSingleton<YouTubeSentMessageTracker>();
        services.TryAddSingleton<IYouTubeSentMessageTracker>(
            static provider => provider.GetRequiredService<YouTubeSentMessageTracker>());
        services.AddSingleton<YouTubeChatMessageMapper>();
        services.AddSingleton<IYouTubeLiveChatStreamClient, YouTubeGrpcLiveChatStreamClient>();
        services.AddSingleton<IYouTubeLiveChatPollingClient, YouTubeRestLiveChatPollingClient>();
        services.AddSingleton<IYouTubeLiveChatSendClient, YouTubeRestLiveChatSendClient>();
        services.AddSingleton<YouTubeSessionContext>();
        services.AddSingleton<YouTubeMessenger>();
        services.AddKeyedSingleton<IPlatformMessenger>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeMessenger>());
        services.AddKeyedSingleton<IHostedComponent>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeMessenger>());
        services.AddSingleton<YouTubeChatIngestionService>();
        services.AddKeyedSingleton<IHostedComponent>(
            StreamingPlatform.YouTube,
            static (provider, _) => provider.GetRequiredService<YouTubeChatIngestionService>());
        services.AddSingleton<IYouTubeChatDiagnostics>(
            static provider => provider.GetRequiredService<YouTubeChatIngestionService>());
        services.AddPlatformHost(StreamingPlatform.YouTube);
        services.AddSingleton<IPlatformConnectionAdapter, YouTubePlatformConnectionAdapter>();

        services.AddSingleton<YouTubeChatPlatform>(static provider => new(
            provider.GetRequiredKeyedService<IStreamStatus>(StreamingPlatform.YouTube),
            provider.GetRequiredService<YouTubeSessionContext>(),
            provider.GetRequiredService<IYouTubeActiveBroadcastSource>(),
            provider.GetRequiredKeyedService<IPlatformMessenger>(StreamingPlatform.YouTube),
            provider.GetRequiredService<YouTubeSettingsStore>(),
            provider.GetRequiredService<YouTubeAccountsStore>(),
            provider.GetRequiredService<ITargetChannelProvider>(),
            provider.GetService<ILogger<YouTubeChatPlatform>>()));
        services.AddSingleton<IChatPlatform>(static provider => provider.GetRequiredService<YouTubeChatPlatform>());

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
