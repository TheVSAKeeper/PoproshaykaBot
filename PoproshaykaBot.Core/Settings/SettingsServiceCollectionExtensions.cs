using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Settings;

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsStores(this IServiceCollection services)
    {
        services.AddSingleton<SettingsWriteGate>();
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<ChatSettingsStore>();
        services.AddSingleton<IChatHistorySettingsProvider>(static provider => provider.GetRequiredService<ChatSettingsStore>());
        services.AddSingleton<AccountsStore>();
        services.AddSingleton<YouTubeSettingsStore>();
        services.AddSingleton<YouTubeAccountsStore>();
        services.AddSingleton<CommandSettingsStore>();
        services.AddSingleton<BroadcastProfilesStore>();
        services.AddSingleton<BroadcastProfilePlatformsStore>();
        services.AddSingleton<PollsStore>();
        services.AddSingleton<RecentCategoriesStore>();
        services.AddSingleton<DashboardLayoutStore>();
        services.AddSingleton<DashboardLayoutCoordinator>();
        services.AddSingleton<ObsChatStore>();
        services.AddSingleton<ObsIntegrationStore>();
        services.AddSingleton<UpdateStore>();
        services.AddSingleton<DebugChannelStore>();
        services.AddSingleton<OnboardingChecklist>();

        services.AddSingleton<ISettingsSnapshotContributor>(static provider => new DelegateSettingsSnapshotContributor(() =>
        [
            new(nameof(AppSettings), SettingsDescriber.Describe(provider.GetRequiredService<SettingsManager>().Current)),
            new("ChatSettings", ChatSettingsStore.Describe(provider.GetRequiredService<ChatSettingsStore>().Load())),
            new("ObsIntegrationSettings", ObsIntegrationStore.Describe(provider.GetRequiredService<ObsIntegrationStore>().Load())),
            new("ObsChatSettings", ObsChatStore.Describe(provider.GetRequiredService<ObsChatStore>().Load())),
            new("UpdateSettings", UpdateStore.Describe(provider.GetRequiredService<UpdateStore>().Load())),
            new("DebugChannelSettings", DebugChannelStore.Describe(provider.GetRequiredService<DebugChannelStore>().Load())),
            new("BroadcastProfilesSettings", BroadcastProfilesStore.Describe(provider.GetRequiredService<BroadcastProfilesStore>().Load())),
            new("BroadcastProfilePlatformsSettings", BroadcastProfilePlatformsStore.Describe(provider.GetRequiredService<BroadcastProfilePlatformsStore>().Load())),
            new("PollsSettings", PollsStore.Describe(provider.GetRequiredService<PollsStore>().Load())),
            new("CommandSettings", CommandSettingsStore.Describe(provider.GetRequiredService<CommandSettingsStore>().Load())),
        ]));

        services.AddSingleton<ISecretValueProvider>(static provider =>
            new DelegateSecretValueProvider("настройки Twitch", () => [provider.GetRequiredService<SettingsManager>().Current.Twitch.ClientSecret]));

        services.AddSingleton<ISecretValueProvider>(static provider =>
            new DelegateSecretValueProvider("настройки OBS", () => [provider.GetRequiredService<ObsIntegrationStore>().Load().Password]));

        return services;
    }
}
