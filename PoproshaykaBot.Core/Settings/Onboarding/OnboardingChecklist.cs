using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Settings.Onboarding;

public sealed class OnboardingChecklist(SettingsManager settingsManager, AccountsStore accountsStore)
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

        if (string.IsNullOrWhiteSpace(twitch.Channel))
        {
            missing.Add("канал");
        }

        if (string.IsNullOrWhiteSpace(accountsStore.Load(TwitchOAuthRole.Bot).AccessToken))
        {
            missing.Add("авторизация бота");
        }

        if (string.IsNullOrWhiteSpace(accountsStore.Load(TwitchOAuthRole.Broadcaster).AccessToken))
        {
            missing.Add("авторизация стримера");
        }

        return missing;
    }
}
