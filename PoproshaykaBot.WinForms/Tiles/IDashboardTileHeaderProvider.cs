namespace PoproshaykaBot.WinForms.Tiles;

public interface IDashboardTileHeaderProvider
{
    IReadOnlyList<ToolStripItem> CreateHeaderItems();
}
