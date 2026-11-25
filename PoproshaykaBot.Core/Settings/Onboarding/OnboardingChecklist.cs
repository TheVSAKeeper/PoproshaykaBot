using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;

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

    public OnboardingStep FirstMissingStep => Check([]);

    public IReadOnlyList<string> GetMissingItems()
    {
        var missing = new List<string>();
        Check(missing);
        return missing;
    }

    private OnboardingStep Check(List<string> missing)
    {
        var twitch = settingsManager.Current.Twitch;
        if (string.IsNullOrWhiteSpace(twitch.ClientId) || string.IsNullOrWhiteSpace(twitch.ClientSecret))
        {
            missing.Add("Client ID/Secret");
        }

        if (channelCatalog?.GetActiveChannel(StreamingPlatform.Twitch) is null && string.IsNullOrWhiteSpace(twitch.Channel))
        {
            missing.Add("канал");
        }

        if (!RedirectUriPortResolver.TryResolve(twitch.RedirectUri, out _))
        {
            missing.Add("адрес перенаправления Twitch");
        }

        var credentialsMissing = missing.Count > 0;
        var botAuthorizationMissing = !HasAuthorization(PlatformAuthRole.Bot);
        if (botAuthorizationMissing)
        {
            missing.Add("авторизация бота");
        }

        var beforeYouTube = missing.Count;
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

        if (credentialsMissing)
        {
            return OnboardingStep.Credentials;
        }

        if (botAuthorizationMissing)
        {
            return OnboardingStep.BotAuthorization;
        }

        return missing.Count > beforeYouTube ? OnboardingStep.YouTube : OnboardingStep.None;
    }

    private bool HasAuthorization(PlatformAuthRole role)
    {
        var account = authorization is null ? accountsStore.Load(role) : authorization.GetAccount(role);
        return TwitchScopes.HasUsableAuthorization(role, account);
    }
}
