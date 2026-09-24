using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public sealed class DashboardLayoutDraft
{
    private const int UndoDepth = 20;
    private const int MaxDepth = 32;
    private const double MinimumShare = 0.05;

    private readonly List<DashboardLayoutSettings> _undo = [];

    private DashboardLayoutSettings _layout;

    public DashboardLayoutDraft(DashboardLayoutSettings? layout)
    {
        _layout = Clone(layout) ?? DashboardLayoutDefaults.Create();
    }

    public DashboardLayoutSettings Layout => _layout;

    public bool CanUndo => _undo.Count > 0;

    public int Version { get; private set; }

    public static DashboardLayoutSettings? Clone(DashboardLayoutSettings? layout)
    {
        return layout is null ? null : JsonStoreClone.DeepClone(layout);
    }

    public void Replace(DashboardLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _layout = layout;
        _undo.Clear();
        Version++;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _layout = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Version++;

        return true;
    }

    public void ResetToDefaults()
    {
        Remember(Clone(_layout)!);

        _layout = DashboardLayoutReconciler.ResetToDefaults(DashboardLayoutDefaults.Create(), _layout);

        DashboardLayoutReconciler.SyncRoot(_layout);
        Version++;
    }

    public IReadOnlyList<string> SetGridSize(int columnCount, int rowCount)
    {
        var columns = Math.Clamp(columnCount, DashboardLayoutDefaults.MinColumnCount, DashboardLayoutDefaults.MaxColumnCount);
        var rows = Math.Clamp(rowCount, DashboardLayoutDefaults.MinRowCount, DashboardLayoutDefaults.MaxRowCount);

        if (columns == _layout.ColumnCount && rows == _layout.RowCount)
        {
            return [];
        }

        var previous = Clone(_layout)!;
        var hidden = Reflow(columns, rows);

        _layout.ColumnCount = columns;
        _layout.RowCount = rows;
        _layout.Root = null;

        DashboardLayoutReconciler.SyncRoot(_layout);

        if (_layout.Root is { } rebuilt && previous.Root is { } before)
        {
            _layout.Root = CarryWeights(rebuilt, before);

            if (!DashboardLayoutReconciler.SyncTiles(_layout))
            {
                _layout.Root = rebuilt;
                DashboardLayoutReconciler.SyncTiles(_layout);
            }
        }

        Remember(previous);
        Version++;

        return hidden;
    }

    public bool Resize(IReadOnlyList<int> path, IReadOnlyList<double> weights)
    {
        return Apply(root => DashboardPaneEditor.TryResize(root, path, weights, out var result) ? result : null)
            == DashboardEditStatus.Applied;
    }

    public DashboardEditStatus Move(IReadOnlyList<int> sourcePath, IReadOnlyList<int> targetPath, PaneSide side)
    {
        return Apply(root => DashboardPaneEditor.TryMove(root, sourcePath, targetPath, side, out var result) ? result : null);
    }

    public DashboardEditStatus Add(string typeId, string targetTypeId, PaneSide side)
    {
        return Apply(root => DashboardPaneEditor.TryFindPath(root, targetTypeId, out var targetPath)
            ? Split(root, targetPath, side, typeId)
            : null);
    }

    public DashboardEditStatus AddAt(string typeId, IReadOnlyList<int> targetPath, PaneSide side)
    {
        return Apply(root => Split(root, targetPath, side, typeId));
    }

    public DashboardRemoveStatus Remove(string typeId)
    {
        return RemoveWhere(root => DashboardPaneEditor.TryFindPath(root, typeId, out var path) ? path : null);
    }

    public DashboardRemoveStatus RemoveAt(IReadOnlyList<int> path)
    {
        return RemoveWhere(_ => path);
    }

    public DashboardPane? Preview(Func<DashboardPane, DashboardPane?> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (_layout.Root is not { } root)
        {
            return null;
        }

        return change(root) is { } updated && Fits(updated) ? updated : null;
    }

    public DashboardEditStatus Apply(Func<DashboardPane, DashboardPane?> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (_layout.Root is not { } root)
        {
            return DashboardEditStatus.Unavailable;
        }

        var previous = Clone(_layout)!;

        if (change(root) is not { } updated)
        {
            _layout = previous;

            return DashboardEditStatus.Rejected;
        }

        if (ReferenceEquals(updated, root))
        {
            return DashboardEditStatus.Applied;
        }

        if (!Fits(updated))
        {
            _layout = previous;

            return DashboardEditStatus.GridFull;
        }

        _layout.Root = updated;

        if (!DashboardLayoutReconciler.SyncTiles(_layout))
        {
            _layout = previous;

            return DashboardEditStatus.Rejected;
        }

        Remember(previous);
        Version++;

        return DashboardEditStatus.Applied;
    }

    private static DashboardPane CarryWeights(DashboardPane rebuilt, DashboardPane previous)
    {
        var carried = rebuilt;

        foreach (var (path, weights) in MatchingWeights(rebuilt, previous, []))
        {
            if (DashboardPaneEditor.TryResize(carried, path, weights, out var resized))
            {
                carried = resized;
            }
        }

        return carried;
    }

    private static IEnumerable<(int[] Path, double[] Weights)> MatchingWeights(DashboardPane rebuilt, DashboardPane previous, int[] path)
    {
        if (path.Length > MaxDepth
            || rebuilt is not SplitPane next
            || previous is not SplitPane before
            || next.Orientation != before.Orientation)
        {
            yield break;
        }

        if (WeightsFor(next, before) is { } weights)
        {
            yield return (path, weights);
        }

        foreach (var (nextIndex, beforeIndex) in Aligned(next, before))
        {
            var nested = MatchingWeights(
                next.Children[nextIndex].Pane,
                before.Children[beforeIndex].Pane,
                [.. path, nextIndex]);

            foreach (var match in nested)
            {
                yield return match;
            }
        }
    }

    private static IEnumerable<(int Next, int Before)> Aligned(SplitPane next, SplitPane before)
    {
        var taken = new bool[before.Children.Count];

        for (var index = 0; index < next.Children.Count; index++)
        {
            var signature = Signature(next.Children[index].Pane);

            if (signature.Count == 0)
            {
                continue;
            }

            for (var candidate = 0; candidate < before.Children.Count; candidate++)
            {
                if (taken[candidate] || !Signature(before.Children[candidate].Pane).SequenceEqual(signature, StringComparer.Ordinal))
                {
                    continue;
                }

                taken[candidate] = true;

                yield return (index, candidate);

                break;
            }
        }
    }

    private static double[]? WeightsFor(SplitPane next, SplitPane before)
    {
        if (next.Children.Count < 2)
        {
            return null;
        }

        var weights = new double[next.Children.Count];
        var carried = 0;
        var total = 0.0;

        foreach (var (nextIndex, beforeIndex) in Aligned(next, before))
        {
            if (before.Children[beforeIndex].Weight is not { } weight || weight <= 0 || !double.IsFinite(weight))
            {
                continue;
            }

            weights[nextIndex] = weight;
            total += weight;
            carried++;
        }

        if (carried == 0 || !double.IsFinite(total) || total <= 0)
        {
            return null;
        }

        var rest = next.Children.Count - carried;

        if (rest == 0)
        {
            return weights;
        }

        var share = Math.Max(1 - total, MinimumShare * rest) / rest;

        for (var index = 0; index < weights.Length; index++)
        {
            if (weights[index] <= 0)
            {
                weights[index] = share;
            }
        }

        return weights;
    }

    private static List<string> Signature(DashboardPane? pane)
    {
        var leaves = new List<string>();

        Collect(pane, leaves, 0);

        return leaves;
    }

    private static void Collect(DashboardPane? pane, List<string> leaves, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        switch (pane)
        {
            case TilePane tile when !DashboardLayoutTree.IsEmptySlot(tile.TypeId):
                leaves.Add(tile.TypeId);

                return;

            case SplitPane split:
                foreach (var slot in split.Children)
                {
                    Collect(slot?.Pane, leaves, depth + 1);
                }

                return;
        }
    }

    private static bool Fits(DashboardPane root)
    {
        return DashboardLayoutTree.TryMeasure(root) is { } size
            && size.Columns <= DashboardLayoutDefaults.MaxColumnCount
            && size.Rows <= DashboardLayoutDefaults.MaxRowCount;
    }

    private List<string> Reflow(int columnCount, int rowCount)
    {
        var pairs = new List<(DashboardTileSettings Tile, PlacedTile Placed)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tile in _layout.Tiles)
        {
            if (!tile.IsVisible
                || string.IsNullOrEmpty(tile.TypeId)
                || DashboardLayoutTree.IsEmptySlot(tile.TypeId)
                || !seen.Add(tile.TypeId))
            {
                continue;
            }

            pairs.Add((tile, new()
            {
                TypeId = tile.TypeId,
                Row = tile.Row,
                Column = tile.Column,
                ColumnSpan = tile.ColumnSpan,
                RowSpan = tile.RowSpan,
                MaxWidth = tile.MaxWidth,
                MaxHeight = tile.MaxHeight,
            }));
        }

        var (_, unplaceable) = DashboardLayoutCalculator.ResolveLayout(pairs.Select(pair => pair.Placed), rowCount, columnCount);
        var lost = unplaceable.ToHashSet();
        var hidden = new List<string>();

        foreach (var (tile, placed) in pairs)
        {
            if (lost.Contains(placed))
            {
                tile.IsVisible = false;
                hidden.Add(tile.TypeId);

                continue;
            }

            tile.Row = placed.Row;
            tile.Column = placed.Column;
            tile.RowSpan = placed.RowSpan;
            tile.ColumnSpan = placed.ColumnSpan;
        }

        return hidden;
    }

    private DashboardPane? Split(DashboardPane root, IReadOnlyList<int> targetPath, PaneSide side, string typeId)
    {
        DashboardLayoutReconciler.AppendMissingTypes(_layout, [typeId]);

        return Place(root, targetPath, side, typeId);
    }

    public static DashboardPane? Place(DashboardPane root, IReadOnlyList<int> targetPath, PaneSide side, string typeId)
    {
        if (side == PaneSide.None && DashboardPaneEditor.TryFill(root, targetPath, typeId, out var filled))
        {
            return filled;
        }

        return DashboardPaneEditor.TrySplit(root, targetPath, side == PaneSide.None ? PaneSide.Right : side, typeId, out var result)
            ? result
            : null;
    }

    private DashboardRemoveStatus RemoveWhere(Func<DashboardPane, IReadOnlyList<int>?> locate)
    {
        var status = DashboardRemoveStatus.Rejected;

        var applied = Apply(root =>
        {
            if (locate(root) is not { } path)
            {
                return null;
            }

            var removal = DashboardPaneEditor.Remove(root, path);

            status = removal.Status;

            return removal.Root;
        });

        if (applied == DashboardEditStatus.Applied)
        {
            return DashboardRemoveStatus.Removed;
        }

        return status == DashboardRemoveStatus.LastTile ? DashboardRemoveStatus.LastTile : DashboardRemoveStatus.Rejected;
    }

    private void Remember(DashboardLayoutSettings previous)
    {
        _undo.Add(previous);

        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }
    }
}
