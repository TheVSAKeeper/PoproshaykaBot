using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Twitch;

internal sealed class TwitchChannelIdentitySource(
    AccountsStore? accountsStore,
    SettingsManager? settingsManager) : IPlatformChannelIdentitySource
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public void CollectKnownChannels(Action<string, IEnumerable<string?>> registerChannel)
    {
        if (accountsStore is null)
        {
            return;
        }

        var broadcaster = accountsStore.Load(PlatformAuthRole.Broadcaster);
        if (!string.IsNullOrWhiteSpace(broadcaster.UserId))
        {
            registerChannel(broadcaster.UserId.Trim(), [broadcaster.Login]);
        }
    }

    public string? GetConfiguredChannelRaw() => settingsManager?.Current.Twitch.Channel;
}
