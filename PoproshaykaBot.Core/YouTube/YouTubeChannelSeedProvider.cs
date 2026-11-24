using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

public sealed class YouTubeChannelSeedProvider(
    YouTubeSettingsStore youtubeSettingsStore,
    YouTubeAccountsStore youtubeAccountsStore) : IPlatformChannelSeedProvider
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public PlatformChannelSeed? TryCreateSeed()
    {
        var ytSettings = youtubeSettingsStore.Load();
        var broadcaster = youtubeAccountsStore.Load(PlatformAuthRole.Broadcaster);
        var rawInput = ytSettings.Channel?.Trim();
        string? channelId = null;
        var handle = string.Empty;

        if (!string.IsNullOrWhiteSpace(rawInput) && YouTubeTargetChannelResolver.TryExtractDirectChannelId(rawInput, out var directId))
        {
            channelId = directId;
            handle = rawInput;
        }
        else if (string.IsNullOrWhiteSpace(rawInput) && !string.IsNullOrWhiteSpace(broadcaster.ChannelId))
        {
            channelId = broadcaster.ChannelId.Trim();
            handle = broadcaster.ChannelTitle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        var displayName = !string.IsNullOrWhiteSpace(broadcaster.ChannelTitle)
            && string.Equals(broadcaster.ChannelId, channelId, StringComparison.Ordinal)
            ? broadcaster.ChannelTitle
            : (!string.IsNullOrWhiteSpace(handle) ? handle : channelId);

        return new(
            StreamingPlatform.YouTube,
            channelId,
            displayName,
            handle,
            $"https://youtube.com/channel/{channelId}",
            MonitoringEnabled: true,
            BroadcasterSubjectId: string.Equals(broadcaster.ChannelId, channelId, StringComparison.Ordinal) ? channelId : null,
            BotSubjectId: youtubeAccountsStore.Load(PlatformAuthRole.Bot).ChannelId);
    }

    public void MirrorActiveChannel(ChannelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Platform != StreamingPlatform.YouTube)
        {
            return;
        }

        var current = youtubeSettingsStore.Load();
        if (!string.Equals(current.Channel, entry.ChannelId, StringComparison.Ordinal))
        {
            youtubeSettingsStore.Mutate(settings => settings.Channel = entry.ChannelId);
        }
    }
}
