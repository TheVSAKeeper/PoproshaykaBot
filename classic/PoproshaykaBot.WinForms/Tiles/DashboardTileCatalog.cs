namespace PoproshaykaBot.WinForms.Tiles;

public sealed class DashboardTileCatalog : IDashboardTileCatalog
{
    private readonly Dictionary<string, DashboardTileType> _byId;

    public DashboardTileCatalog(IEnumerable<DashboardTileType> tiles)
    {
        All = tiles.ToArray();
        _byId = All.ToDictionary(type => type.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<DashboardTileType> All { get; }

    public DashboardTileType? Find(string? typeId)
    {
        return string.IsNullOrWhiteSpace(typeId) ? null : _byId.GetValueOrDefault(typeId);
    }
}
