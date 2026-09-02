using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Core.Dashboard;

public static class DashboardLayoutTree
{
    private const int MaxCells = 4096;
    private const int MaxDepth = 32;

    public static DashboardPane? TryBuild(IEnumerable<DashboardTileSettings> tiles, int columnCount, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        var rects = CollectVisible(tiles, columnCount, rowCount);

        return rects is null ? null : Build(rects, 0, 0, rowCount, columnCount);
    }

    public static IReadOnlyList<TileRect>? TryProject(DashboardPane root, int columnCount, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (columnCount <= 0 || rowCount <= 0)
        {
            return null;
        }

        var projected = new List<TileRect>();

        return Project(root, 0, 0, rowCount, columnCount, projected) ? projected : null;
    }

    public static GridSize? TryMeasure(DashboardPane root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Measure(root, 0);
    }

    private static GridSize? Measure(DashboardPane? pane, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (pane)
        {
            case TilePane tile:
                return string.IsNullOrEmpty(tile.TypeId) ? null : new GridSize(1, 1);

            case SplitPane { Orientation: SplitOrientation.Columns or SplitOrientation.Rows, Children.Count: > 1 } split:
                return MeasureSplit(split, depth);

            default:
                return null;
        }
    }

    private static GridSize? MeasureSplit(SplitPane split, int depth)
    {
        var alongColumns = split.Orientation == SplitOrientation.Columns;
        var columns = 0;
        var rows = 0;

        foreach (var slot in split.Children)
        {
            if (slot is null || Measure(slot.Pane, depth + 1) is not { } size)
            {
                return null;
            }

            columns = alongColumns ? columns + size.Columns : Math.Max(columns, size.Columns);
            rows = alongColumns ? Math.Max(rows, size.Rows) : rows + size.Rows;
        }

        return new(columns, rows);
    }

    private static List<TileRect>? CollectVisible(IEnumerable<DashboardTileSettings> tiles, int columnCount, int rowCount)
    {
        if (columnCount <= 0 || rowCount <= 0 || (long)columnCount * rowCount > MaxCells)
        {
            return null;
        }

        var rects = new List<TileRect>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var occupied = new bool[rowCount, columnCount];

        foreach (var tile in tiles)
        {
            if (tile is null || !tile.IsVisible)
            {
                continue;
            }

            if (string.IsNullOrEmpty(tile.TypeId))
            {
                return null;
            }

            var rect = new TileRect(tile.TypeId, tile.Row, tile.Column, tile.RowSpan, tile.ColumnSpan);

            if (rect.RowSpan < 1 || rect.ColumnSpan < 1
                || rect.Row < 0 || rect.Column < 0
                || rect.RowEnd > rowCount || rect.ColumnEnd > columnCount
                || !seen.Add(rect.TypeId))
            {
                return null;
            }

            if (!TryOccupy(occupied, rect))
            {
                return null;
            }

            rects.Add(rect);
        }

        if (rects.Count == 0)
        {
            return null;
        }

        foreach (var covered in occupied)
        {
            if (!covered)
            {
                return null;
            }
        }

        return rects;
    }

    private static bool TryOccupy(bool[,] occupied, TileRect rect)
    {
        for (var row = rect.Row; row < rect.RowEnd; row++)
        {
            for (var column = rect.Column; column < rect.ColumnEnd; column++)
            {
                if (occupied[row, column])
                {
                    return false;
                }

                occupied[row, column] = true;
            }
        }

        return true;
    }

    private static DashboardPane? Build(List<TileRect> rects, int row, int column, int rowEnd, int columnEnd)
    {
        if (rects.Count == 1)
        {
            return new TilePane(rects[0].TypeId);
        }

        var columnCuts = FindColumnCuts(rects, column, columnEnd);

        if (columnCuts.Count > 0)
        {
            return BuildSplit(rects, SplitOrientation.Columns, columnCuts, row, column, rowEnd, columnEnd);
        }

        var rowCuts = FindRowCuts(rects, row, rowEnd);

        return rowCuts.Count > 0
            ? BuildSplit(rects, SplitOrientation.Rows, rowCuts, row, column, rowEnd, columnEnd)
            : null;
    }

    private static List<int> FindColumnCuts(List<TileRect> rects, int start, int end)
    {
        var cuts = new List<int>();

        for (var cut = start + 1; cut < end; cut++)
        {
            if (rects.TrueForAll(rect => rect.Column >= cut || rect.ColumnEnd <= cut))
            {
                cuts.Add(cut);
            }
        }

        return cuts;
    }

    private static List<int> FindRowCuts(List<TileRect> rects, int start, int end)
    {
        var cuts = new List<int>();

        for (var cut = start + 1; cut < end; cut++)
        {
            if (rects.TrueForAll(rect => rect.Row >= cut || rect.RowEnd <= cut))
            {
                cuts.Add(cut);
            }
        }

        return cuts;
    }

    private static SplitPane? BuildSplit(
        List<TileRect> rects,
        SplitOrientation orientation,
        List<int> cuts,
        int row,
        int column,
        int rowEnd,
        int columnEnd)
    {
        var alongColumns = orientation == SplitOrientation.Columns;
        var start = alongColumns ? column : row;
        var end = alongColumns ? columnEnd : rowEnd;
        var total = (double)(end - start);

        var boundaries = new List<int> { start };
        boundaries.AddRange(cuts);
        boundaries.Add(end);

        var children = new List<PaneSlot>(boundaries.Count - 1);

        for (var index = 0; index < boundaries.Count - 1; index++)
        {
            var from = boundaries[index];
            var to = boundaries[index + 1];

            var inside = rects.FindAll(rect => alongColumns
                ? rect.Column >= from && rect.ColumnEnd <= to
                : rect.Row >= from && rect.RowEnd <= to);

            var pane = alongColumns
                ? Build(inside, row, from, rowEnd, to)
                : Build(inside, from, column, to, columnEnd);

            if (pane is null)
            {
                return null;
            }

            children.Add(new(pane, (to - from) / total));
        }

        return new SplitPane(orientation, children);
    }

    private static bool Project(DashboardPane pane, int row, int column, int rowEnd, int columnEnd, List<TileRect> projected)
    {
        if (rowEnd <= row || columnEnd <= column)
        {
            return false;
        }

        switch (pane)
        {
            case TilePane tile:
                if (string.IsNullOrEmpty(tile.TypeId))
                {
                    return false;
                }

                projected.Add(new(tile.TypeId, row, column, rowEnd - row, columnEnd - column));
                return true;

            case SplitPane { Orientation: SplitOrientation.Columns or SplitOrientation.Rows, Children.Count: > 1 } split:
                return ProjectSplit(split, row, column, rowEnd, columnEnd, projected);

            default:
                return false;
        }
    }

    private static bool ProjectSplit(SplitPane split, int row, int column, int rowEnd, int columnEnd, List<TileRect> projected)
    {
        var alongColumns = split.Orientation == SplitOrientation.Columns;
        var start = alongColumns ? column : row;
        var end = alongColumns ? columnEnd : rowEnd;
        var sizes = Distribute(split.Children, split.Orientation, end - start);

        if (sizes is null)
        {
            return false;
        }

        var offset = start;

        for (var index = 0; index < split.Children.Count; index++)
        {
            var next = offset + sizes[index];

            var placed = alongColumns
                ? Project(split.Children[index].Pane, row, offset, rowEnd, next, projected)
                : Project(split.Children[index].Pane, offset, column, next, columnEnd, projected);

            if (!placed)
            {
                return false;
            }

            offset = next;
        }

        return true;
    }

    private static double ShareForUnknown(double known, int count, int unknown)
    {
        if (known <= 0)
        {
            return 1.0 / count;
        }

        return known < 1
            ? (1 - known) / unknown
            : known / (count - unknown);
    }

    private static int[]? Distribute(IReadOnlyList<PaneSlot> children, SplitOrientation orientation, int total)
    {
        var minimums = new int[children.Count];
        var required = 0;

        for (var index = 0; index < children.Count; index++)
        {
            if (children[index] is not { Pane: not null } slot || Measure(slot.Pane, 0) is not { } size)
            {
                return null;
            }

            minimums[index] = orientation == SplitOrientation.Columns ? size.Columns : size.Rows;
            required += minimums[index];
        }

        if (total < required)
        {
            return null;
        }

        var weights = ResolveWeights(children);

        return weights is null ? null : LayOut(weights, total, minimums);
    }

    public static double[]? ResolveWeights(IReadOnlyList<PaneSlot> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var count = children.Count;
        var weights = new double[count];
        var known = 0.0;
        var unknown = 0;

        for (var index = 0; index < count; index++)
        {
            if (children[index] is not { Pane: not null })
            {
                return null;
            }

            if (children[index].Weight is { } weight && weight > 0 && double.IsFinite(weight))
            {
                weights[index] = weight;
                known += weight;
            }
            else
            {
                weights[index] = double.NaN;
                unknown++;
            }
        }

        if (unknown == 0)
        {
            return weights;
        }

        var share = ShareForUnknown(known, count, unknown);

        for (var index = 0; index < count; index++)
        {
            if (double.IsNaN(weights[index]))
            {
                weights[index] = share;
            }
        }

        return weights;
    }

    private static int[] LayOut(double[] weights, int total, int[] minimums)
    {
        var count = weights.Length;
        var sum = weights.Sum();

        if (!double.IsFinite(sum) || sum <= 0)
        {
            Array.Fill(weights, 1.0);
            sum = count;
        }

        var sizes = new int[count];
        var cumulative = 0.0;
        var offset = 0;
        var reserved = minimums.Sum();

        for (var index = 0; index < count; index++)
        {
            cumulative += weights[index] / sum;
            reserved -= minimums[index];

            var boundary = index == count - 1
                ? total
                : (int)Math.Round(cumulative * total, MidpointRounding.AwayFromZero);

            boundary = Math.Clamp(boundary, offset + minimums[index], total - reserved);
            sizes[index] = boundary - offset;
            offset = boundary;
        }

        return sizes;
    }
}
