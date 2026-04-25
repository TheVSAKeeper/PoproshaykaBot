namespace PoproshaykaBot.WinForms.Tiles;

public static class DashboardTileCatalog
{
    public static readonly IReadOnlyList<DashboardTileType> All =
    [
        StreamInfoTileType.Instance,
        BroadcastStatusTileType.Instance,
        LogsTileType.Instance,
        TwitchChatTileType.Instance,
        ChatOverlayPreviewTileType.Instance,
        BroadcastProfilesTileType.Instance,
    ];

    private static readonly Dictionary<string, DashboardTileType> ById =
        All.ToDictionary(type => type.Id, StringComparer.Ordinal);

    public static DashboardTileType? Find(string? typeId)
    {
        return string.IsNullOrWhiteSpace(typeId) ? null : ById.GetValueOrDefault(typeId);
    }
}
