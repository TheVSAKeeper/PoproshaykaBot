namespace PoproshaykaBot.Core.Infrastructure.Diagnostics;

public interface ISettingsSnapshotContributor
{
    IEnumerable<SettingsSnapshotEntry> CollectSnapshots();
}
