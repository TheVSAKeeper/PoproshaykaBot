using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tiles;

public static class DashboardLayoutDefaults
{
    public const int DefaultColumnCount = 4;
    public const int DefaultRowCount = 4;

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

        AddTile(layout, StreamInfoTileType.Instance, 0, 0, 2, 1);
        AddTile(layout, BroadcastStatusTileType.Instance, 0, 2, 2, 1);
        AddTile(layout, TwitchChatTileType.Instance, 1, 0, 2, 3);
        AddTile(layout, LogsTileType.Instance, 1, 2, 2, 1);
        AddTile(layout, ChatOverlayPreviewTileType.Instance, 2, 2, 1, 1);
        AddTile(layout, BroadcastProfilesTileType.Instance, 2, 3, 1, 1);
        AddTile(layout, PollsControlTileType.Instance, 3, 2, 2, 1);

        return layout;
    }

    private static void AddTile(DashboardLayoutSettings layout, DashboardTileType type, int row, int column, int columnSpan, int rowSpan)
    {
        layout.Tiles.Add(new()
        {
            Id = type.Id,
            TypeId = type.Id,
            Order = layout.Tiles.Count,
            Row = row,
            Column = column,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            IsVisible = true,
        });
    }
}
