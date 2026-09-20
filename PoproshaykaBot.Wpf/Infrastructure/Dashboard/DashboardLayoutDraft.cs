using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public sealed class DashboardLayoutDraft
{
    private const int UndoDepth = 20;

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

    public bool Resize(IReadOnlyList<int> path, IReadOnlyList<double> weights)
    {
        return Apply(root => DashboardPaneEditor.TryResize(root, path, weights, out var result) ? result : null)
            == DashboardEditStatus.Applied;
    }

    public DashboardEditStatus Swap(IReadOnlyList<int> firstPath, IReadOnlyList<int> secondPath)
    {
        return Apply(root => DashboardPaneEditor.TrySwap(root, firstPath, secondPath, out var result) ? result : null);
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

    private static bool Fits(DashboardPane root)
    {
        return DashboardLayoutTree.TryMeasure(root) is { } size
            && size.Columns <= DashboardLayoutDefaults.MaxColumnCount
            && size.Rows <= DashboardLayoutDefaults.MaxRowCount;
    }

    private DashboardPane? Split(DashboardPane root, IReadOnlyList<int> targetPath, PaneSide side, string typeId)
    {
        DashboardLayoutReconciler.AppendMissingTypes(_layout, [typeId]);

        return DashboardPaneEditor.TrySplit(root, targetPath, side, typeId, out var result) ? result : null;
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
