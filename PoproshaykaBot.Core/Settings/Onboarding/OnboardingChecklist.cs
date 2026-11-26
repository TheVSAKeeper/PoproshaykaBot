using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Settings.Onboarding;

public enum OnboardingMissingItem
{
    None = 0,
    Platforms = 1,
    TwitchCredentials = 2,
    TwitchChannel = 3,
    TwitchRedirectUri = 4,
    TwitchBotAuthorization = 5,
    YouTubeClientId = 6,
    YouTubeChannel = 7,
    YouTubeBotAuthorization = 8,
}

public sealed class OnboardingChecklist(
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ChannelCatalogStore? channelCatalog = null,
    TwitchChannelAuthorization? authorization = null,
    YouTubeSettingsStore? youtubeSettings = null,
    YouTubeAccountsStore? youtubeAccounts = null)
{
    public bool IsComplete => GetMissingReasons().Count == 0;

    public bool HasMissingTwitchBotAuthorization => GetMissingReasons().Contains(OnboardingMissingItem.TwitchBotAuthorization);

    public bool HasMissingYouTubeBotAuthorization => GetMissingReasons().Contains(OnboardingMissingItem.YouTubeBotAuthorization);

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

    public IReadOnlyList<OnboardingMissingItem> GetMissingReasons()
    {
        var missing = new List<OnboardingMissingItem>();
        Check(missing);
        return missing;
    }

    public IReadOnlyList<string> GetMissingItems()
    {
        return [.. GetMissingReasons().Select(FormatMissingItem)];
    }

    private OnboardingStep Check(List<OnboardingMissingItem> missing)
    {
        var twitch = settingsManager.Current.Twitch;
        var twitchEnabled = twitch.Enabled;
        var youtube = youtubeSettings?.Load();
        var youtubeEnabled = youtube?.Enabled == true;

        if (!twitchEnabled && !youtubeEnabled)
        {
            missing.Add(OnboardingMissingItem.Platforms);
            return OnboardingStep.Platforms;
        }

        var twitchCredentialsMissing = false;
        var twitchBotAuthorizationMissing = false;
        if (twitchEnabled)
        {
            var beforeTwitchCredentials = missing.Count;
            if (string.IsNullOrWhiteSpace(twitch.ClientId) || string.IsNullOrWhiteSpace(twitch.ClientSecret))
            {
                missing.Add(OnboardingMissingItem.TwitchCredentials);
            }

            var activeTwitchChannel = channelCatalog?.GetActiveChannel(StreamingPlatform.Twitch);
            if (activeTwitchChannel is null && string.IsNullOrWhiteSpace(twitch.Channel))
            {
                missing.Add(OnboardingMissingItem.TwitchChannel);
            }

            if (!RedirectUriPortResolver.TryResolve(twitch.RedirectUri, out _))
            {
                missing.Add(OnboardingMissingItem.TwitchRedirectUri);
            }

            twitchCredentialsMissing = missing.Count > beforeTwitchCredentials;
            twitchBotAuthorizationMissing = !HasAuthorization(PlatformAuthRole.Bot, activeTwitchChannel);
            if (twitchBotAuthorizationMissing)
            {
                missing.Add(OnboardingMissingItem.TwitchBotAuthorization);
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
                missing.Add(OnboardingMissingItem.YouTubeClientId);
            }

            if (channel is null && string.IsNullOrWhiteSpace(youtube.Channel))
            {
                missing.Add(OnboardingMissingItem.YouTubeChannel);
            }

            if (!YouTubeScopes.HasUsableAuthorization(PlatformAuthRole.Bot, bot))
            {
                missing.Add(OnboardingMissingItem.YouTubeBotAuthorization);
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

    private static string FormatMissingItem(OnboardingMissingItem item)
    {
        return item switch
        {
            OnboardingMissingItem.Platforms => "площадки трансляции",
            OnboardingMissingItem.TwitchCredentials => "Client ID/Secret",
            OnboardingMissingItem.TwitchChannel => "канал",
            OnboardingMissingItem.TwitchRedirectUri => "адрес перенаправления Twitch",
            OnboardingMissingItem.TwitchBotAuthorization => "авторизация бота",
            OnboardingMissingItem.YouTubeClientId => "Client ID YouTube",
            OnboardingMissingItem.YouTubeChannel => "канал YouTube",
            OnboardingMissingItem.YouTubeBotAuthorization => "авторизация бота YouTube",
            _ => string.Empty,
        };
    }

    private bool HasAuthorization(PlatformAuthRole role, ChannelCatalogEntry? activeChannel)
    {
        var account = authorization is null
            ? accountsStore.Load(role, activeChannel?.BotContextId)
            : authorization.GetAccount(role);
        return TwitchScopes.HasUsableAuthorization(role, account);
    }
}
