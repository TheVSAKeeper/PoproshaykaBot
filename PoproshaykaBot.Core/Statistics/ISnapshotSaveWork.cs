namespace PoproshaykaBot.Core.Statistics;

public interface ISnapshotSaveWork
{
    Task CommitAsync(CancellationToken cancellationToken);

    void Rollback();
}
