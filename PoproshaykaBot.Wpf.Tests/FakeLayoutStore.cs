using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests;

internal sealed class FakeLayoutStore(DashboardLayoutSettings? persisted)
    : DashboardLayoutStore(null, Path.Combine(Path.GetTempPath(), $"dashboard-layout-{Guid.NewGuid():N}.json"))
{
    private DashboardLayoutSettings? _current = persisted;

    public DashboardLayoutSettings? Saved { get; private set; }

    public int SaveCount { get; private set; }

    public override DashboardLayoutSettings? LoadDashboard()
    {
        return JsonStoreClone.DeepCloneNullable(_current);
    }

    public override void SaveDashboard(DashboardLayoutSettings layout)
    {
        Saved = layout;
        _current = JsonStoreClone.DeepClone(layout);
        SaveCount++;
    }
}
