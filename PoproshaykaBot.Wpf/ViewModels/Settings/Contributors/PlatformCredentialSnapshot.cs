using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

internal readonly record struct PlatformCredentialSnapshot(
    string ClientId,
    string ClientSecret,
    string BotAccessToken,
    string BotRefreshToken,
    string BroadcasterAccessToken,
    string BroadcasterRefreshToken)
{
    public IReadOnlyList<SettingsSectionEvent> CollectAuthorizationRefreshes(
        StreamingPlatform platform,
        PlatformCredentialSnapshot current)
    {
        var events = new List<SettingsSectionEvent>();
        var appCredentialsChanged = !string.Equals(ClientId, current.ClientId, StringComparison.Ordinal)
            || !string.Equals(ClientSecret, current.ClientSecret, StringComparison.Ordinal);

        if (ShouldNotifyRole(appCredentialsChanged, BotAccessToken, BotRefreshToken, current.BotAccessToken, current.BotRefreshToken))
        {
            events.Add(SettingsSectionEvent.Create(new PlatformAuthorizationRefreshed(platform, PlatformAuthRole.Bot)));
        }

        if (ShouldNotifyRole(
            appCredentialsChanged,
            BroadcasterAccessToken,
            BroadcasterRefreshToken,
            current.BroadcasterAccessToken,
            current.BroadcasterRefreshToken))
        {
            events.Add(SettingsSectionEvent.Create(new PlatformAuthorizationRefreshed(platform, PlatformAuthRole.Broadcaster)));
        }

        return events;
    }

    private static bool ShouldNotifyRole(
        bool appCredentialsChanged,
        string previousAccessToken,
        string previousRefreshToken,
        string currentAccessToken,
        string currentRefreshToken)
    {
        var roleTokenChanged = !string.Equals(previousAccessToken, currentAccessToken, StringComparison.Ordinal)
            || !string.Equals(previousRefreshToken, currentRefreshToken, StringComparison.Ordinal);

        if (roleTokenChanged)
        {
            return true;
        }

        return appCredentialsChanged
            && (!string.IsNullOrWhiteSpace(currentAccessToken) || !string.IsNullOrWhiteSpace(currentRefreshToken));
    }
}
