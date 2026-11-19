using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch;

namespace PoproshaykaBot.Core.Settings.Onboarding;

public sealed class OnboardingChecklist(
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ChannelCatalogStore? channelCatalog = null,
    TwitchChannelAuthorization? authorization = null,
    YouTubeSettingsStore? youtubeSettings = null,
    YouTubeAccountsStore? youtubeAccounts = null)
{
    public bool IsComplete => GetMissingItems().Count == 0;

    public bool RequiresWizard => string.IsNullOrWhiteSpace(settingsManager.Current.Twitch.ClientId);

    public IReadOnlyList<string> GetMissingItems()
    {
        var twitch = settingsManager.Current.Twitch;
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(twitch.ClientId) || string.IsNullOrWhiteSpace(twitch.ClientSecret))
        {
            missing.Add("Client ID/Secret");
        }

        if (channelCatalog?.GetActiveChannel(StreamingPlatform.Twitch) is null && string.IsNullOrWhiteSpace(twitch.Channel))
        {
            missing.Add("канал");
        }

        if (!HasAuthorization(PlatformAuthRole.Bot))
        {
            missing.Add("авторизация бота");
        }

        if (youtubeSettings?.Load() is { Enabled: true } youtube)
        {
            var channel = channelCatalog?.GetActiveChannel(StreamingPlatform.YouTube);
            var bot = youtubeAccounts?.Load(PlatformAuthRole.Bot, channel?.BotContextId);
            if (string.IsNullOrWhiteSpace(youtube.ClientId))
            {
                missing.Add("Client ID YouTube");
            }

            if (channel is null && string.IsNullOrWhiteSpace(youtube.Channel))
            {
                missing.Add("канал YouTube");
            }

            if (!PlatformAuthRoles.HasCredentials(bot?.AccessToken, bot?.RefreshToken))
            {
                missing.Add("авторизация бота YouTube");
            }
        }

        return missing;
    }

    private bool HasAuthorization(PlatformAuthRole role)
    {
        var account = authorization is null ? accountsStore.Load(role) : authorization.GetAccount(role);
        return PlatformAuthRoles.HasCredentials(account?.AccessToken, account?.RefreshToken);
    }
}
