using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.YouTube;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class KnownChannelIdentityResolver
{
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly IReadOnlyList<IPlatformChannelIdentitySource> _identitySources;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelIdentitySource> _sourcesByPlatform;

    public KnownChannelIdentityResolver(
        ChannelCatalogStore? channelCatalogStore = null,
        AccountsStore? twitchAccountsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        SettingsManager? settingsManager = null,
        YouTubeSettingsStore? youtubeSettingsStore = null)
        : this(
            channelCatalogStore,
            BuildLegacySources(twitchAccountsStore, youtubeAccountsStore, settingsManager, youtubeSettingsStore))
    {
    }

    public KnownChannelIdentityResolver(
        ChannelCatalogStore? channelCatalogStore,
        IEnumerable<IPlatformChannelIdentitySource>? identitySources)
    {
        _channelCatalogStore = channelCatalogStore;
        _identitySources = identitySources?.ToArray() ?? [];
        _sourcesByPlatform = _identitySources.ToDictionary(static source => source.Platform);
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

        foreach (var source in _identitySources)
        {
            source.CollectKnownChannels((channelId, aliases) =>
            {
                if (string.IsNullOrWhiteSpace(channelId))
                {
                    return;
                }

                var trimmedId = channelId.Trim();
                knownChannels.Add(new(source.Platform, trimmedId));
                foreach (var alias in aliases)
                {
                    RegisterAlias(map, ambiguous, source.Platform, alias, trimmedId);
                }
            });
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

        foreach (var source in _identitySources)
        {
            CheckConfiguredChannel(
                source,
                source.GetConfiguredChannelRaw(),
                map,
                ambiguous,
                knownChannels,
                hasUnresolvedConfigured);
        }

        return new(map, ambiguous, knownChannels, hasUnresolvedConfigured, _sourcesByPlatform);
    }

    private static IReadOnlyList<IPlatformChannelIdentitySource> BuildLegacySources(
        AccountsStore? twitchAccountsStore,
        YouTubeAccountsStore? youtubeAccountsStore,
        SettingsManager? settingsManager,
        YouTubeSettingsStore? youtubeSettingsStore)
    {
        var list = new List<IPlatformChannelIdentitySource>(2);
        if (twitchAccountsStore is not null || settingsManager is not null)
        {
            list.Add(new TwitchChannelIdentitySource(twitchAccountsStore, settingsManager));
        }

        if (youtubeAccountsStore is not null || youtubeSettingsStore is not null)
        {
            list.Add(new YouTubeChannelIdentitySource(youtubeAccountsStore, youtubeSettingsStore));
        }

        return list;
    }

    private void CheckConfiguredChannel(
        IPlatformChannelIdentitySource source,
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

        var platform = source.Platform;
        if (source.TryExtractDirectChannelId(rawChannel, out var directId))
        {
            knownChannels.Add(new(platform, directId));
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

    private void RegisterAlias(
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

    internal string? NormalizeAlias(StreamingPlatform platform, string? rawAlias)
    {
        return NormalizeAlias(_sourcesByPlatform, platform, rawAlias);
    }

    private static string? NormalizeAlias(
        IReadOnlyDictionary<StreamingPlatform, IPlatformChannelIdentitySource> sourcesByPlatform,
        StreamingPlatform platform,
        string? rawAlias)
    {
        if (string.IsNullOrWhiteSpace(rawAlias))
        {
            return null;
        }

        if (sourcesByPlatform.TryGetValue(platform, out var source))
        {
            return source.NormalizeAlias(rawAlias);
        }

        return rawAlias.Trim().TrimStart('@').ToLowerInvariant();
    }

    internal sealed class ChannelIdentitySnapshot(
        Dictionary<(StreamingPlatform Platform, string Alias), string> map,
        HashSet<(StreamingPlatform Platform, string Alias)> ambiguous,
        HashSet<ChannelKey> knownChannels,
        HashSet<StreamingPlatform> hasUnresolvedConfigured,
        IReadOnlyDictionary<StreamingPlatform, IPlatformChannelIdentitySource> sourcesByPlatform)
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

            if (sourcesByPlatform.TryGetValue(platform, out var source)
                && source.TryExtractDirectChannelId(channelName, out var directId))
            {
                return new ChannelKey(platform, directId);
            }

            var normalized = NormalizeAlias(sourcesByPlatform, platform, channelName);
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
