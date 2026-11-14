namespace PoproshaykaBot.Core.Statistics;

public interface IPeriodicStatisticsUnit
{
    bool IsLoaded { get; }

    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    Task LoadForStartAsync(CancellationToken cancellationToken) => EnsureLoadedAsync(cancellationToken);

    ISnapshotSaveWork? PrepareSave(bool force);

    void OnStarted()
    {
    }
}
