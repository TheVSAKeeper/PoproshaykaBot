using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

internal static class PlatformEnablementSources
{
    public static IReadOnlyDictionary<StreamingPlatform, IPlatformEnablementSource> BuildMap(
        IEnumerable<IPlatformEnablementSource>? enablementSources,
        IPlatformEnablementSource? fallbackSource)
    {
        var map = new Dictionary<StreamingPlatform, IPlatformEnablementSource>();
        if (enablementSources is not null)
        {
            foreach (var source in enablementSources)
            {
                map[source.Platform] = source;
            }
        }

        if (fallbackSource is not null && !map.ContainsKey(fallbackSource.Platform))
        {
            map[fallbackSource.Platform] = fallbackSource;
        }

        return map;
    }

    public static IReadOnlyList<IPlatformEnablementSource> BuildList(
        IEnumerable<IPlatformEnablementSource>? enablementSources,
        IPlatformEnablementSource? fallbackSource) =>
        [.. BuildMap(enablementSources, fallbackSource).Values];
}
