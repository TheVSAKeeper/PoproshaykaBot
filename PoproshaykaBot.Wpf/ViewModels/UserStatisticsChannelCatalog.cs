using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

internal sealed record KnownStatisticsChannel(ChannelKey Key, string DisplayName, string FullLabel);

internal static class UserStatisticsChannelCatalog
{
    public static IReadOnlyList<KnownStatisticsChannel> BuildChannels(
        IUserStatisticsRepository userStatistics,
        ChannelCatalogStore? channelCatalog,
        bool includeCatalogChannels,
        out ChannelsSettings? catalogSettings)
    {
        var channels = new HashSet<ChannelKey>(userStatistics.GetKnownChannels());
        catalogSettings = channelCatalog?.Load();

        if (includeCatalogChannels && catalogSettings is not null)
        {
            foreach (var entry in catalogSettings.Channels)
            {
                if (Enum.IsDefined(entry.Platform) && !string.IsNullOrWhiteSpace(entry.ChannelId))
                {
                    channels.Add(new ChannelKey(entry.Platform, entry.ChannelId.Trim()));
                }
            }
        }

        var settings = catalogSettings;
        return channels
            .Select(channel =>
            {
                var displayName = ResolveChannelName(channel, channelCatalog, settings);
                var fullLabel = $"{PlatformVisuals.GetDisplayName(channel.Platform)} · {displayName}";
                return new KnownStatisticsChannel(channel, displayName, fullLabel);
            })
            .OrderBy(static item => item.Key.Platform)
            .ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ResolveChannelName(
        ChannelKey channel,
        ChannelCatalogStore? channelCatalog,
        ChannelsSettings? catalogSettings)
    {
        var fromCatalog = channelCatalog?.ResolveDisplayName(channel);
        if (!string.IsNullOrWhiteSpace(fromCatalog))
        {
            return fromCatalog;
        }

        var fromSettings = catalogSettings?.Channels.FirstOrDefault(c =>
            c.Platform == channel.Platform && string.Equals(c.ChannelId, channel.ChannelId, StringComparison.Ordinal))?.DisplayName;
        return string.IsNullOrWhiteSpace(fromSettings) ? channel.ChannelId : fromSettings;
    }
}
