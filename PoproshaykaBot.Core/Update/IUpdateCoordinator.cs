namespace PoproshaykaBot.Core.Update;

public interface IUpdateCoordinator
{
    UpdateKind Kind { get; }

    bool IsUpdatable { get; }

    string DefaultRepositorySlug { get; }

    Version CurrentVersion { get; }

    UpdateCandidate? LatestCandidate { get; }

    bool HasPreparedUpdate { get; }

    Task<UpdateCandidate?> CheckNowAsync(CancellationToken cancellationToken);

    Task PrepareAsync(UpdateCandidate candidate, IProgress<int>? progress, CancellationToken cancellationToken);

    void SkipVersion(string version);
}
