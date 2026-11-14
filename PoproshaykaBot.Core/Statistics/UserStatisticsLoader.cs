using System.Collections.Frozen;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsLoader
{
    private readonly FrozenDictionary<StreamingPlatform, PlatformUserStatisticsUnit> _units;

    internal UserStatisticsLoader(IEnumerable<PlatformUserStatisticsUnit> units)
    {
        _units = units.ToFrozenDictionary(static unit => unit.Platform);
    }

    public bool IsAnyLoaded => _units.Values.Any(static unit => unit.IsLoaded);

    public bool IsLoadedFor(StreamingPlatform platform) => GetUnit(platform).IsLoaded;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var unit in _units.Values)
        {
            await unit.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask<bool> InvalidateAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
    {
        return GetUnit(platform).InvalidateAsync(cancellationToken);
    }

    public ValueTask RestoreAsync(StreamingPlatform platform, bool wasLoaded)
    {
        return GetUnit(platform).RestoreAsync(wasLoaded);
    }

    private PlatformUserStatisticsUnit GetUnit(StreamingPlatform platform)
    {
        return _units.TryGetValue(platform, out var unit)
            ? unit
            : throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
    }
}
