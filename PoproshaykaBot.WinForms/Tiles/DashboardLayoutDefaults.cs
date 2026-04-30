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

        AddTile(layout, StreamInfoTileType.TypeId, 0, 0, 2, 1);
        AddTile(layout, BroadcastStatusTileType.TypeId, 0, 2, 2, 1);
        AddTile(layout, TwitchChatTileType.TypeId, 1, 0, 2, 3);
        AddTile(layout, LogsTileType.TypeId, 1, 2, 2, 1);
        AddTile(layout, ChatOverlayPreviewTileType.TypeId, 2, 2, 1, 1);
        AddTile(layout, BroadcastProfilesTileType.TypeId, 2, 3, 1, 1);
        AddTile(layout, PollsControlTileType.TypeId, 3, 2, 2, 1);

        return layout;
    }

    private static void AddTile(DashboardLayoutSettings layout, string typeId, int row, int column, int columnSpan, int rowSpan)
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
            IsVisible = true,
        });
    }
}
