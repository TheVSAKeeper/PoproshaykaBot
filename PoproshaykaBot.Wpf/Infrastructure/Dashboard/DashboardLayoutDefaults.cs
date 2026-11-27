using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public static class DashboardLayoutDefaults
{
    public const int DefaultColumnCount = 2;
    public const int DefaultRowCount = 4;

    public const int MinColumnCount = 1;
    public const int MaxColumnCount = 8;
    public const int MinRowCount = 1;
    public const int MaxRowCount = 8;

    public static DashboardLayoutSettings Create()
    {
        return DashboardLayoutPresets.CreateCompact();
    }
}
