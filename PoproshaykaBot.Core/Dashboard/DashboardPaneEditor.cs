using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Core.Dashboard;

public static class DashboardPaneEditor
{
    private const int MaxDepth = 32;
    private const double MinimumWeight = 0.05;

    public static bool TrySplit(DashboardPane root, string targetTypeId, PaneSide side, string typeId, out DashboardPane result)
    {
        ArgumentNullException.ThrowIfNull(root);

        result = root;

        if (side == PaneSide.None
            || string.IsNullOrEmpty(targetTypeId)
            || string.IsNullOrEmpty(typeId)
            || !TryCollectLeaves(root, out var leaves)
            || !leaves.Contains(targetTypeId)
            || leaves.Contains(typeId))
        {
            return false;
        }

        var orientation = side is PaneSide.Left or PaneSide.Right ? SplitOrientation.Columns : SplitOrientation.Rows;
        var before = side is PaneSide.Left or PaneSide.Top;
        if (Insert(root, targetTypeId, orientation, before, typeId) is not { } split || Normalize(split) is not { } normalized)
        {
            return false;
        }

        result = normalized;

        return true;
    }

    public static bool TrySwap(DashboardPane root, string firstTypeId, string secondTypeId, out DashboardPane result)
    {
        ArgumentNullException.ThrowIfNull(root);

        result = root;

        if (string.IsNullOrEmpty(firstTypeId)
            || string.IsNullOrEmpty(secondTypeId)
            || string.Equals(firstTypeId, secondTypeId, StringComparison.Ordinal)
            || !TryCollectLeaves(root, out var leaves)
            || !leaves.Contains(firstTypeId)
            || !leaves.Contains(secondTypeId))
        {
            return false;
        }

        result = Exchange(root, firstTypeId, secondTypeId);

        return true;
    }

    public static bool TryRemove(DashboardPane root, string typeId, out DashboardPane? result)
    {
        ArgumentNullException.ThrowIfNull(root);

        result = root;

        if (string.IsNullOrEmpty(typeId) || !TryCollectLeaves(root, out var leaves) || !leaves.Contains(typeId))
        {
            return false;
        }

        if (root is TilePane)
        {
            result = null;

            return true;
        }

        if (root is not SplitPane split
            || !TryRemoveIn(split, typeId, out var replacement)
            || Normalize(replacement) is not { } normalized)
        {
            result = root;

            return false;
        }

        result = normalized;

        return true;
    }

    public static bool TryResize(DashboardPane root, IReadOnlyList<int> path, IReadOnlyList<double> weights, out DashboardPane result)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(weights);

        result = root;

        if (weights.Count < 2 || !IsWellFormed(root))
        {
            return false;
        }

        foreach (var weight in weights)
        {
            if (!IsExplicit(weight))
            {
                return false;
            }
        }

        if (Reweight(root, path, 0, Balance(weights)) is not { } resized || !IsWellFormed(resized))
        {
            return false;
        }

        result = resized;

        return true;
    }

    public static DashboardPane? Normalize(DashboardPane root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return TryCollectLeaves(root, out _) ? NormalizeCore(root) : null;
    }

    public static bool IsWellFormed(DashboardPane root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return TryCollectLeaves(root, out _);
    }

    private static bool TryCollectLeaves(DashboardPane root, out HashSet<string> leaves)
    {
        leaves = new(StringComparer.Ordinal);

        return Collect(root, leaves, 0);
    }

    private static bool Collect(DashboardPane? pane, HashSet<string> leaves, int depth)
    {
        if (depth > MaxDepth)
        {
            return false;
        }

        switch (pane)
        {
            case TilePane tile:
                return !string.IsNullOrEmpty(tile.TypeId) && leaves.Add(tile.TypeId);

            case SplitPane { Orientation: SplitOrientation.Columns or SplitOrientation.Rows } split:
                if (split.Children is not { Count: > 1 })
                {
                    return false;
                }

                foreach (var slot in split.Children)
                {
                    if (slot is null || !Collect(slot.Pane, leaves, depth + 1))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static DashboardPane? Insert(DashboardPane pane, string target, SplitOrientation orientation, bool before, string typeId)
    {
        if (pane is TilePane tile)
        {
            return string.Equals(tile.TypeId, target, StringComparison.Ordinal)
                ? new SplitPane(orientation, Pair(new PaneSlot(tile, 0.5), new(new TilePane(typeId), 0.5), before))
                : null;
        }

        var split = (SplitPane)pane;

        for (var index = 0; index < split.Children.Count; index++)
        {
            var slot = split.Children[index];

            if (slot.Pane is TilePane leaf
                && string.Equals(leaf.TypeId, target, StringComparison.Ordinal)
                && split.Orientation == orientation)
            {
                var half = slot.Weight is { } weight ? weight / 2 : (double?)null;
                var children = new List<PaneSlot>(split.Children);

                children.RemoveAt(index);
                children.InsertRange(index, Pair(new PaneSlot(leaf, half), new(new TilePane(typeId), half), before));

                return new SplitPane(orientation, children);
            }

            var replaced = Insert(slot.Pane, target, orientation, before, typeId);

            if (replaced is null)
            {
                continue;
            }

            var updated = new List<PaneSlot>(split.Children);
            updated[index] = slot with { Pane = replaced };

            return Build(split.Orientation, updated);
        }

        return null;
    }

    private static List<PaneSlot> Pair(PaneSlot existing, PaneSlot inserted, bool before)
    {
        return before ? [inserted, existing] : [existing, inserted];
    }

    private static DashboardPane Exchange(DashboardPane pane, string first, string second)
    {
        if (pane is TilePane tile)
        {
            if (string.Equals(tile.TypeId, first, StringComparison.Ordinal))
            {
                return new TilePane(second);
            }

            return string.Equals(tile.TypeId, second, StringComparison.Ordinal) ? new TilePane(first) : tile;
        }

        var split = (SplitPane)pane;
        var children = new List<PaneSlot>(split.Children.Count);

        foreach (var slot in split.Children)
        {
            children.Add(slot with { Pane = Exchange(slot.Pane, first, second) });
        }

        return new SplitPane(split.Orientation, children);
    }

    private static bool TryRemoveIn(SplitPane split, string typeId, out DashboardPane replacement)
    {
        replacement = split;

        for (var index = 0; index < split.Children.Count; index++)
        {
            var slot = split.Children[index];

            if (slot.Pane is TilePane leaf && string.Equals(leaf.TypeId, typeId, StringComparison.Ordinal))
            {
                var remaining = new List<PaneSlot>(split.Children);
                remaining.RemoveAt(index);

                replacement = Build(split.Orientation, remaining);

                return true;
            }

            if (slot.Pane is not SplitPane nested || !TryRemoveIn(nested, typeId, out var inner))
            {
                continue;
            }

            var children = new List<PaneSlot>(split.Children);
            children[index] = slot with { Pane = inner };

            replacement = Build(split.Orientation, children);

            return true;
        }

        return false;
    }

    private static DashboardPane Build(SplitOrientation orientation, List<PaneSlot> slots)
    {
        var flat = new List<PaneSlot>(slots.Count);

        foreach (var slot in slots)
        {
            if (slot.Pane is SplitPane nested && nested.Orientation == orientation && SpliceTotal(slot.Weight, nested.Children) is { } total)
            {
                foreach (var child in nested.Children)
                {
                    flat.Add(new(child.Pane, slot.Weight!.Value * child.Weight!.Value / total));
                }

                continue;
            }

            flat.Add(slot);
        }

        return flat.Count == 1 ? flat[0].Pane : new SplitPane(orientation, flat);
    }

    private static double? SpliceTotal(double? weight, IReadOnlyList<PaneSlot> children)
    {
        if (!IsExplicit(weight))
        {
            return null;
        }

        var total = 0.0;

        foreach (var child in children)
        {
            if (!IsExplicit(child.Weight))
            {
                return null;
            }

            total += child.Weight!.Value;
        }

        return IsExplicit(total) ? total : null;
    }

    private static bool IsExplicit(double? weight)
    {
        return weight is { } value && value > 0 && double.IsFinite(value);
    }

    private static double[] Balance(IReadOnlyList<double> weights)
    {
        var count = weights.Count;
        var balanced = new double[count];
        var total = 0.0;

        foreach (var weight in weights)
        {
            total += weight;
        }

        if (!double.IsFinite(total) || total <= 0 || MinimumWeight * count >= 1)
        {
            Array.Fill(balanced, 1.0 / count);

            return balanced;
        }

        var deficit = 0.0;
        var free = 0.0;

        for (var index = 0; index < count; index++)
        {
            balanced[index] = weights[index] / total;

            if (balanced[index] < MinimumWeight)
            {
                deficit += MinimumWeight - balanced[index];
            }
            else
            {
                free += balanced[index] - MinimumWeight;
            }
        }

        if (deficit <= 0)
        {
            return balanced;
        }

        var scale = (free - deficit) / free;

        for (var index = 0; index < count; index++)
        {
            balanced[index] = balanced[index] < MinimumWeight
                ? MinimumWeight
                : MinimumWeight + ((balanced[index] - MinimumWeight) * scale);
        }

        return balanced;
    }

    private static DashboardPane? Reweight(DashboardPane pane, IReadOnlyList<int> path, int depth, IReadOnlyList<double> weights)
    {
        if (pane is not SplitPane split)
        {
            return null;
        }

        if (depth == path.Count)
        {
            if (split.Children.Count != weights.Count)
            {
                return null;
            }

            var slots = new PaneSlot[weights.Count];

            for (var index = 0; index < weights.Count; index++)
            {
                slots[index] = new(split.Children[index].Pane, weights[index]);
            }

            return new SplitPane(split.Orientation, slots);
        }

        var position = path[depth];

        if (position < 0
            || position >= split.Children.Count
            || Reweight(split.Children[position].Pane, path, depth + 1, weights) is not { } rebuilt)
        {
            return null;
        }

        var children = split.Children.ToArray();

        children[position] = new(rebuilt, split.Children[position].Weight);

        return new SplitPane(split.Orientation, children);
    }

    private static DashboardPane NormalizeCore(DashboardPane pane)
    {
        if (pane is not SplitPane split)
        {
            return pane;
        }

        var count = split.Children.Count;
        var weights = new double?[count];
        var known = 0.0;
        var explicitCount = 0;

        for (var index = 0; index < count; index++)
        {
            if (!IsExplicit(split.Children[index].Weight))
            {
                continue;
            }

            weights[index] = split.Children[index].Weight;
            known += split.Children[index].Weight!.Value;
            explicitCount++;
        }

        var scale = Scale(known, explicitCount, count);
        var children = new List<PaneSlot>(count);

        for (var index = 0; index < count; index++)
        {
            var scaled = weights[index] * scale;

            children.Add(new(NormalizeCore(split.Children[index].Pane), IsExplicit(scaled) ? scaled : null));
        }

        return new SplitPane(split.Orientation, children);
    }

    private static double Scale(double known, int explicitCount, int count)
    {
        if (explicitCount == 0 || known <= 0)
        {
            return 1;
        }

        if (explicitCount == count)
        {
            return 1 / known;
        }

        return known >= 1 ? explicitCount / (double)count / known : 1;
    }
}
