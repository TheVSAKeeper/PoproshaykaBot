namespace PoproshaykaBot.WinForms.Tiles;

public interface IDashboardTileCatalog
{
    IReadOnlyList<DashboardTileType> All { get; }

    DashboardTileType? Find(string? typeId);
}
