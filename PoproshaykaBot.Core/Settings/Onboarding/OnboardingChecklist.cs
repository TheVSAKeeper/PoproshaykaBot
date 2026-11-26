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

    public bool RequiresWizard
    {
        get
        {
            var twitch = settingsManager.Current.Twitch;
            var twitchEnabled = twitch.Enabled;
            var youtube = youtubeSettings?.Load();
            var youtubeEnabled = youtube?.Enabled == true;

            if (!twitchEnabled && !youtubeEnabled)
            {
                return true;
            }

            var twitchMissingClientId = !twitchEnabled || string.IsNullOrWhiteSpace(twitch.ClientId);
            var youtubeMissingClientId = !youtubeEnabled || string.IsNullOrWhiteSpace(youtube?.ClientId);
            return twitchMissingClientId && youtubeMissingClientId;
        }
    }

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
        var twitchEnabled = twitch.Enabled;
        var youtube = youtubeSettings?.Load();
        var youtubeEnabled = youtube?.Enabled == true;

        if (!twitchEnabled && !youtubeEnabled)
        {
            missing.Add("площадки трансляции");
            return OnboardingStep.Platforms;
        }

        var twitchCredentialsMissing = false;
        var twitchBotAuthorizationMissing = false;
        if (twitchEnabled)
        {
            var beforeTwitchCredentials = missing.Count;
            if (string.IsNullOrWhiteSpace(twitch.ClientId) || string.IsNullOrWhiteSpace(twitch.ClientSecret))
            {
                missing.Add("Client ID/Secret");
            }

            var activeTwitchChannel = channelCatalog?.GetActiveChannel(StreamingPlatform.Twitch);
            if (activeTwitchChannel is null && string.IsNullOrWhiteSpace(twitch.Channel))
            {
                missing.Add("канал");
            }

            if (!RedirectUriPortResolver.TryResolve(twitch.RedirectUri, out _))
            {
                missing.Add("адрес перенаправления Twitch");
            }

            twitchCredentialsMissing = missing.Count > beforeTwitchCredentials;
            twitchBotAuthorizationMissing = !HasAuthorization(PlatformAuthRole.Bot, activeTwitchChannel);
            if (twitchBotAuthorizationMissing)
            {
                missing.Add("авторизация бота");
            }
        }

        var youtubeMissing = false;
        if (youtubeEnabled)
        {
            var beforeYouTube = missing.Count;
            var channel = channelCatalog?.GetActiveChannel(StreamingPlatform.YouTube);
            var bot = youtubeAccounts?.Load(PlatformAuthRole.Bot, channel?.BotContextId);
            if (string.IsNullOrWhiteSpace(youtube!.ClientId))
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

            youtubeMissing = missing.Count > beforeYouTube;
        }

        if (twitchCredentialsMissing)
        {
            return OnboardingStep.Credentials;
        }

        if (twitchBotAuthorizationMissing)
        {
            return OnboardingStep.BotAuthorization;
        }

        return youtubeMissing ? OnboardingStep.YouTube : OnboardingStep.None;
    }

    private bool HasAuthorization(PlatformAuthRole role, ChannelCatalogEntry? activeChannel)
    {
        var account = authorization is null
            ? accountsStore.Load(role, activeChannel?.BotContextId)
            : authorization.GetAccount(role);
        return TwitchScopes.HasUsableAuthorization(role, account);
    }
}
