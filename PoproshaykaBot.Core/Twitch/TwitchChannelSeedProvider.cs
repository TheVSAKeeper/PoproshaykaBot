using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Twitch;

public sealed class TwitchChannelSeedProvider(
    SettingsManager settingsManager,
    AccountsStore twitchAccountsStore,
    ITargetChannelProvider targetChannelProvider) : IPlatformChannelSeedProvider
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public PlatformChannelSeed? TryCreateSeed()
    {
        var twitchChannel = settingsManager.Current.Twitch.Channel?.Trim();
        if (string.IsNullOrWhiteSpace(twitchChannel))
        {
            return null;
        }

        var broadcaster = twitchAccountsStore.Load(PlatformAuthRole.Broadcaster);
        if (string.IsNullOrWhiteSpace(broadcaster.UserId)
            || !string.Equals(broadcaster.Login, twitchChannel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new(
            StreamingPlatform.Twitch,
            broadcaster.UserId.Trim(),
            broadcaster.Login,
            broadcaster.Login,
            $"https://twitch.tv/{broadcaster.Login}",
            MonitoringEnabled: true,
            BroadcasterSubjectId: broadcaster.UserId.Trim(),
            BotSubjectId: twitchAccountsStore.Load(PlatformAuthRole.Bot).UserId);
    }

    public void MirrorActiveChannel(ChannelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Platform != StreamingPlatform.Twitch || targetChannelProvider.Current.IsDebugSession)
        {
            return;
        }

        var targetLogin = !string.IsNullOrWhiteSpace(entry.Handle) ? entry.Handle : entry.DisplayName;
        if (!string.IsNullOrWhiteSpace(targetLogin)
            && !string.Equals(settingsManager.Current.Twitch.Channel, targetLogin, StringComparison.OrdinalIgnoreCase))
        {
            settingsManager.Mutate(settings => settings.Twitch.Channel = targetLogin);
        }
    }
}
