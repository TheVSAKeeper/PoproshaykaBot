using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class KnownChannelIdentityResolver
{
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly AccountsStore? _twitchAccountsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;

    public KnownChannelIdentityResolver(
        ChannelCatalogStore? channelCatalogStore = null,
        AccountsStore? twitchAccountsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        SettingsManager? settingsManager = null,
        YouTubeSettingsStore? youtubeSettingsStore = null)
    {
        _channelCatalogStore = channelCatalogStore;
        _twitchAccountsStore = twitchAccountsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _settingsManager = settingsManager;
        _youtubeSettingsStore = youtubeSettingsStore;
    }

    public ChannelIdentitySnapshot BuildSnapshot(
        BroadcastHistoryDocument? history,
        ActiveBroadcast? activeBroadcast)
    {
        var map = new Dictionary<(StreamingPlatform Platform, string Alias), string>();
        var ambiguous = new HashSet<(StreamingPlatform Platform, string Alias)>();
        var knownChannels = new HashSet<ChannelKey>();

        if (_channelCatalogStore is not null)
        {
            var settings = _channelCatalogStore.Load();
            foreach (var entry in settings.Channels)
            {
                if (!Enum.IsDefined(entry.Platform) || string.IsNullOrWhiteSpace(entry.ChannelId))
                {
                    continue;
                }

                var key = new ChannelKey(entry.Platform, entry.ChannelId.Trim());
                knownChannels.Add(key);
                RegisterAlias(map, ambiguous, entry.Platform, entry.ChannelId, key.ChannelId);
                RegisterAlias(map, ambiguous, entry.Platform, entry.DisplayName, key.ChannelId);
                foreach (var alias in entry.KnownAliases)
                {
                    RegisterAlias(map, ambiguous, entry.Platform, alias, key.ChannelId);
                }
            }
        }

        if (_twitchAccountsStore is not null)
        {
            var broadcaster = _twitchAccountsStore.Load(PlatformAuthRole.Broadcaster);
            if (!string.IsNullOrWhiteSpace(broadcaster.UserId))
            {
                var id = broadcaster.UserId.Trim();
                knownChannels.Add(new(StreamingPlatform.Twitch, id));
                RegisterAlias(map, ambiguous, StreamingPlatform.Twitch, broadcaster.Login, id);
            }
        }

        if (_youtubeAccountsStore is not null)
        {
            var broadcaster = _youtubeAccountsStore.Load(PlatformAuthRole.Broadcaster);
            if (!string.IsNullOrWhiteSpace(broadcaster.ChannelId))
            {
                var id = broadcaster.ChannelId.Trim();
                knownChannels.Add(new(StreamingPlatform.YouTube, id));
                RegisterAlias(map, ambiguous, StreamingPlatform.YouTube, id, id);
                RegisterAlias(map, ambiguous, StreamingPlatform.YouTube, broadcaster.ChannelTitle, id);
            }
        }

        if (history is not null)
        {
            foreach (var broadcast in history.Broadcasts)
            {
                foreach (var platformRecord in broadcast.Platforms)
                {
                    if (!Enum.IsDefined(platformRecord.Platform) || string.IsNullOrWhiteSpace(platformRecord.ChannelId))
                    {
                        continue;
                    }

                    var id = platformRecord.ChannelId.Trim();
                    knownChannels.Add(new(platformRecord.Platform, id));
                    RegisterAlias(map, ambiguous, platformRecord.Platform, id, id);
                    RegisterAlias(map, ambiguous, platformRecord.Platform, platformRecord.Channel, id);
                }
            }
        }

        if (activeBroadcast is not null)
        {
            foreach (var platformRecord in activeBroadcast.Platforms)
            {
                if (!Enum.IsDefined(platformRecord.Platform) || string.IsNullOrWhiteSpace(platformRecord.ChannelId))
                {
                    continue;
                }

                var id = platformRecord.ChannelId.Trim();
                knownChannels.Add(new(platformRecord.Platform, id));
                RegisterAlias(map, ambiguous, platformRecord.Platform, id, id);
                RegisterAlias(map, ambiguous, platformRecord.Platform, platformRecord.Channel, id);
            }
        }

        var hasUnresolvedConfigured = new HashSet<StreamingPlatform>();

        if (_settingsManager is not null)
        {
            var twitchChannel = _settingsManager.Current.Twitch.Channel;
            CheckConfiguredChannel(
                StreamingPlatform.Twitch,
                twitchChannel,
                map,
                ambiguous,
                knownChannels,
                hasUnresolvedConfigured);
        }

        if (_youtubeSettingsStore is not null)
        {
            var youtubeChannel = _youtubeSettingsStore.Load().Channel;
            CheckConfiguredChannel(
                StreamingPlatform.YouTube,
                youtubeChannel,
                map,
                ambiguous,
                knownChannels,
                hasUnresolvedConfigured);
        }

        return new(map, ambiguous, knownChannels, hasUnresolvedConfigured);
    }

    private static void CheckConfiguredChannel(
        StreamingPlatform platform,
        string? rawChannel,
        Dictionary<(StreamingPlatform Platform, string Alias), string> map,
        HashSet<(StreamingPlatform Platform, string Alias)> ambiguous,
        HashSet<ChannelKey> knownChannels,
        HashSet<StreamingPlatform> hasUnresolvedConfigured)
    {
        if (string.IsNullOrWhiteSpace(rawChannel))
        {
            return;
        }

        if (platform == StreamingPlatform.YouTube
            && YouTubeTargetChannelResolver.TryExtractDirectChannelId(rawChannel, out var directId))
        {
            knownChannels.Add(new(StreamingPlatform.YouTube, directId));
            return;
        }

        var normalized = NormalizeAlias(platform, rawChannel);
        if (normalized is null || ambiguous.Contains((platform, normalized)) || !map.TryGetValue((platform, normalized), out var resolvedId))
        {
            hasUnresolvedConfigured.Add(platform);
            return;
        }

        knownChannels.Add(new(platform, resolvedId));
    }

    private static void RegisterAlias(
        Dictionary<(StreamingPlatform Platform, string Alias), string> map,
        HashSet<(StreamingPlatform Platform, string Alias)> ambiguous,
        StreamingPlatform platform,
        string? rawAlias,
        string channelId)
    {
        var normalized = NormalizeAlias(platform, rawAlias);
        if (normalized is null || ambiguous.Contains((platform, normalized)))
        {
            return;
        }

        if (map.TryGetValue((platform, normalized), out var existingId))
        {
            if (!string.Equals(existingId, channelId, StringComparison.Ordinal))
            {
                map.Remove((platform, normalized));
                ambiguous.Add((platform, normalized));
            }

            return;
        }

        map[(platform, normalized)] = channelId;
    }

    internal static string? NormalizeAlias(StreamingPlatform platform, string? rawAlias)
    {
        if (string.IsNullOrWhiteSpace(rawAlias))
        {
            return null;
        }

        var trimmed = rawAlias.Trim();
        if (platform == StreamingPlatform.YouTube && YouTubeTargetChannelResolver.NormalizeHandle(trimmed) is { } handle)
        {
            return handle.TrimStart('@').ToLowerInvariant();
        }

        return trimmed.TrimStart('@').ToLowerInvariant();
    }

    internal sealed class ChannelIdentitySnapshot(
        Dictionary<(StreamingPlatform Platform, string Alias), string> map,
        HashSet<(StreamingPlatform Platform, string Alias)> ambiguous,
        HashSet<ChannelKey> knownChannels,
        HashSet<StreamingPlatform> hasUnresolvedConfigured)
    {
        public ChannelKey? Resolve(StreamingPlatform platform, string? channelId, string? channelName)
        {
            if (!Enum.IsDefined(platform))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(channelId))
            {
                return new ChannelKey(platform, channelId.Trim());
            }

            if (string.IsNullOrWhiteSpace(channelName))
            {
                return null;
            }

            if (platform == StreamingPlatform.YouTube
                && YouTubeTargetChannelResolver.TryExtractDirectChannelId(channelName, out var directId))
            {
                return new ChannelKey(StreamingPlatform.YouTube, directId);
            }

            var normalized = NormalizeAlias(platform, channelName);
            if (normalized is null || ambiguous.Contains((platform, normalized)))
            {
                return null;
            }

            return map.TryGetValue((platform, normalized), out var resolvedId)
                ? new ChannelKey(platform, resolvedId)
                : null;
        }

        public ChannelKey? GetSingleConfiguredChannel(StreamingPlatform platform)
        {
            if (hasUnresolvedConfigured.Contains(platform))
            {
                return null;
            }

            ChannelKey? single = null;
            foreach (var key in knownChannels)
            {
                if (key.Platform != platform)
                {
                    continue;
                }

                if (single is not null && single.Value != key)
                {
                    return null;
                }

                single = key;
            }

            return single;
        }
    }
}
