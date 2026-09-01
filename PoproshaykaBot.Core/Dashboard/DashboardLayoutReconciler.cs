using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Core.Dashboard;

public static class DashboardLayoutReconciler
{
    public static DashboardTileSettings CreateHiddenTile(PlacedTile placed, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        return new()
        {
            Id = placed.TypeId,
            TypeId = placed.TypeId,
            Row = placed.Row,
            Column = placed.Column,
            ColumnSpan = placed.ColumnSpan,
            RowSpan = placed.RowSpan,
            IsVisible = false,
            IsCollapsed = isCollapsed,
            MaxHeight = placed.MaxHeight,
            MaxWidth = placed.MaxWidth,
        };
    }

    public static bool AppendMissingTypes(DashboardLayoutSettings layout, IEnumerable<string> catalogTypeIds)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(catalogTypeIds);

        var present = layout.Tiles.Select(tile => tile.TypeId).ToHashSet(StringComparer.Ordinal);
        var order = NextOrder(layout);
        var appended = false;

        foreach (var typeId in catalogTypeIds)
        {
            if (string.IsNullOrEmpty(typeId) || !present.Add(typeId))
            {
                continue;
            }

            layout.Tiles.Add(new()
            {
                Id = typeId,
                TypeId = typeId,
                Order = order++,
                IsVisible = false,
            });

            appended = true;
        }

        return appended;
    }

    public static void AppendPreserved(DashboardLayoutSettings layout, IEnumerable<DashboardTileSettings> preserved)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(preserved);

        var written = layout.Tiles.Select(tile => tile.TypeId).ToHashSet(StringComparer.Ordinal);
        var order = NextOrder(layout);

        foreach (var tile in preserved)
        {
            if (!written.Add(tile.TypeId))
            {
                continue;
            }

            var clone = JsonStoreClone.DeepClone(tile);
            clone.Order = order++;
            layout.Tiles.Add(clone);
        }
    }

    public static DashboardLayoutSettings ResetToDefaults(DashboardLayoutSettings defaults, DashboardLayoutSettings? persisted)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var layout = JsonStoreClone.DeepClone(defaults);

        if (persisted is not null)
        {
            AppendPreserved(layout, persisted.Tiles);
        }

        return layout;
    }

    // TODO: сливается только IsCollapsed, остальные поля черновик настроек по-прежнему перетирает; когда дашборд начнёт писать в файл ещё одно поле мимо страницы настроек – заменить слияние на перечитывание файла перед сохранением
    public static void MergeCollapseState(DashboardLayoutSettings layout, DashboardLayoutSettings? persisted)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (persisted is null)
        {
            return;
        }

        var collapsed = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var tile in persisted.Tiles)
        {
            collapsed.TryAdd(tile.TypeId, tile.IsCollapsed);
        }

        foreach (var tile in layout.Tiles)
        {
            if (collapsed.TryGetValue(tile.TypeId, out var isCollapsed))
            {
                tile.IsCollapsed = isCollapsed;
            }
        }
    }

    public static void SyncRoot(DashboardLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Root is not null && ProjectsInto(layout.Root, layout))
        {
            return;
        }

        layout.Root = DashboardLayoutTree.TryBuild(layout.Tiles, layout.ColumnCount, layout.RowCount);
    }

    public static string DescribeHiddenTiles(IReadOnlyCollection<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        if (titles.Count == 0)
        {
            return string.Empty;
        }

        var names = string.Join(", ", titles.Select(title => $"«{title}»"));

        return titles.Count == 1
            ? $"Плитка {names} не поместилась в сетку и выключена – верните её из палитры."
            : $"Плитки не поместились в сетку и выключены: {names} – верните их из палитры.";
    }

    private static bool ProjectsInto(DashboardPane root, DashboardLayoutSettings layout)
    {
        var expected = layout.Tiles
            .Where(tile => tile is { IsVisible: true })
            .Select(tile => new TileRect(tile.TypeId, tile.Row, tile.Column, tile.RowSpan, tile.ColumnSpan))
            .ToList();

        if (expected.Select(rect => rect.TypeId).Distinct(StringComparer.Ordinal).Count() != expected.Count)
        {
            return false;
        }

        var projected = DashboardLayoutTree.TryProject(root, layout.ColumnCount, layout.RowCount);

        return projected is not null
               && projected.Count == expected.Count
               && projected.All(expected.Remove);
    }

    private static int NextOrder(DashboardLayoutSettings layout)
    {
        return layout.Tiles.Count == 0 ? 0 : layout.Tiles.Max(tile => tile.Order) + 1;
    }
}
