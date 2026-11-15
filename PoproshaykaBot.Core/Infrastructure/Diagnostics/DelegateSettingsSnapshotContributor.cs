namespace PoproshaykaBot.Core.Infrastructure.Diagnostics;

public sealed class DelegateSettingsSnapshotContributor(Func<IEnumerable<SettingsSnapshotEntry>> collect)
    : ISettingsSnapshotContributor
{
    public IEnumerable<SettingsSnapshotEntry> CollectSnapshots()
    {
        return collect();
    }
}
