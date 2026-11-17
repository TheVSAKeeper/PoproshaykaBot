using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsLoader
{
    private readonly UserStatisticsCatalogPersistenceUnit _unit;

    internal UserStatisticsLoader(UserStatisticsCatalogPersistenceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _unit = unit;
    }

    public bool IsLoaded => _unit.IsLoaded;

    public bool IsAnyLoaded => _unit.IsLoaded;

    public bool IsLoadedFor(StreamingPlatform platform) => _unit.IsLoaded;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        return _unit.EnsureLoadedAsync(cancellationToken);
    }

    public ValueTask<bool> InvalidateAsync(CancellationToken cancellationToken = default)
    {
        return _unit.InvalidateAsync(cancellationToken);
    }

    public ValueTask<bool> InvalidateAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
    {
        return _unit.InvalidateAsync(cancellationToken);
    }

    public ValueTask RestoreAsync(bool wasLoaded)
    {
        return _unit.RestoreAsync(wasLoaded);
    }

    public ValueTask RestoreAsync(StreamingPlatform platform, bool wasLoaded)
    {
        return _unit.RestoreAsync(wasLoaded);
    }
}
