using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

internal sealed class YouTubeChannelIdentitySource(
    YouTubeAccountsStore? accountsStore,
    YouTubeSettingsStore? settingsStore) : IPlatformChannelIdentitySource
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public void CollectKnownChannels(Action<string, IEnumerable<string?>> registerChannel)
    {
        if (accountsStore is null)
        {
            return;
        }

        var broadcaster = accountsStore.Load(PlatformAuthRole.Broadcaster);
        if (!string.IsNullOrWhiteSpace(broadcaster.ChannelId))
        {
            var id = broadcaster.ChannelId.Trim();
            registerChannel(id, [id, broadcaster.ChannelTitle]);
        }
    }

    public string? GetConfiguredChannelRaw() => settingsStore?.Load().Channel;

    public bool TryExtractDirectChannelId(string rawChannel, out string channelId) =>
        YouTubeTargetChannelResolver.TryExtractDirectChannelId(rawChannel, out channelId);

    public string? NormalizeAlias(string rawAlias)
    {
        if (string.IsNullOrWhiteSpace(rawAlias))
        {
            return null;
        }

        var trimmed = rawAlias.Trim();
        if (YouTubeTargetChannelResolver.NormalizeHandle(trimmed) is { } handle)
        {
            return handle.TrimStart('@').ToLowerInvariant();
        }

        return trimmed.TrimStart('@').ToLowerInvariant();
    }
}
