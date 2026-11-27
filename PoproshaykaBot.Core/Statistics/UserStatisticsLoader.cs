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

    internal IPeriodicStatisticsUnit PersistenceUnit => _unit;

    public StatisticsLoadState LoadState => _unit.LoadState;

    public bool IsLoaded => _unit.IsLoaded;

    public bool IsAnyLoaded => _unit.IsLoaded;

    public bool IsLoadedFor(StreamingPlatform platform) => _unit.IsLoaded;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        return _unit.EnsureLoadedAsync(cancellationToken);
    }
}
