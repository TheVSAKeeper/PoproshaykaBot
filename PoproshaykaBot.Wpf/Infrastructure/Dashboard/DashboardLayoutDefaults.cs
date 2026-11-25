using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public static class DashboardLayoutDefaults
{
    public const int DefaultColumnCount = 4;
    public const int DefaultRowCount = 5;

    public const int MinColumnCount = 1;
    public const int MaxColumnCount = 8;
    public const int MinRowCount = 1;
    public const int MaxRowCount = 8;

    public static DashboardLayoutSettings Create()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = DefaultColumnCount,
            RowCount = DefaultRowCount,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "broadcast-status", 1, 0, 1, 3);
        AddTile(layout, "broadcast-profiles", 4, 0, 1, 1);
        AddTile(layout, "polls-control", 3, 0, 1, 1, isVisible: false);
        AddTile(layout, "obs-info", 0, 1, 1, 3);
        AddTile(layout, "stream-preview", 0, 1, 1, 2, isVisible: false);
        AddTile(layout, "chat-overlay-preview", 3, 1, 1, 2);
        AddTile(layout, "twitch-chat", 0, 2, 1, 5);
        AddTile(layout, "multiplatform-chat", 0, 3, 1, 3);
        AddTile(layout, "multistream", 3, 3, 1, 2);

        return layout;
    }

    private static void AddTile(
        DashboardLayoutSettings layout,
        string typeId,
        int row,
        int column,
        int columnSpan,
        int rowSpan,
        bool isVisible = true)
    {
        layout.Tiles.Add(new()
        {
            Id = typeId,
            TypeId = typeId,
            Order = layout.Tiles.Count,
            Row = row,
            Column = column,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            IsVisible = isVisible,
        });
    }
}
