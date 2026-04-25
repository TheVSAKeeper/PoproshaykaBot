using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tiles;

public static class DashboardLayoutDefaults
{
    public const int ColumnCount = 4;
    public const int MaxRowSpan = 3;

    public static DashboardLayoutSettings Create()
    {
        var layout = new DashboardLayoutSettings();

        AddTile(layout, StreamInfoTileType.Instance, 2, 1);
        AddTile(layout, BroadcastStatusTileType.Instance, 2, 1);
        AddTile(layout, TwitchChatTileType.Instance, 2, 2);
        AddTile(layout, LogsTileType.Instance, 2, 2);
        AddTile(layout, ChatOverlayPreviewTileType.Instance, 2, 2);
        AddTile(layout, BroadcastProfilesTileType.Instance, 2, 2);

        return layout;
    }

    private static void AddTile(DashboardLayoutSettings layout, DashboardTileType type, int columnSpan, int rowSpan)
    {
        layout.Tiles.Add(new()
        {
            Id = type.Id,
            TypeId = type.Id,
            Order = layout.Tiles.Count,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            IsVisible = true,
        });
    }
}
