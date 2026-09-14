using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public sealed class DashboardEditSession : IDisposable
{
    public static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(500);

    private const int UndoDepth = 20;

    private readonly DashboardLayoutCoordinator _coordinator;
    private readonly ILogger? _logger;
    private readonly SynchronizationContext? _context;
    private readonly List<DashboardLayoutSettings> _undo = [];
    private readonly ITimer _timer;
    private readonly object _gate = new();

    private DashboardLayoutSettings _draft;
    private int _baseRevision;
    private bool _dirty;
    private bool _writing;
    private bool _disposed;

    public DashboardEditSession(DashboardLayoutCoordinator coordinator, TimeProvider time, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(time);

        _coordinator = coordinator;
        _logger = logger;
        _context = SynchronizationContext.Current;

        var snapshot = coordinator.Read();

        _draft = Clone(snapshot.Layout) ?? DashboardLayoutDefaults.Create();
        _baseRevision = snapshot.Revision;

        _timer = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _coordinator.LayoutChanged += OnLayoutChanged;
    }

    public event EventHandler? Changed;

    public DashboardLayoutSettings Draft => _draft;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count > 0;
            }
        }
    }

    public bool Resize(IReadOnlyList<int> path, IReadOnlyList<double> weights)
    {
        return Apply(root => DashboardPaneEditor.TryResize(root, path, weights, out var result) ? result : null);
    }

    public bool Swap(string firstTypeId, string secondTypeId)
    {
        return Apply(root => DashboardPaneEditor.TrySwap(root, firstTypeId, secondTypeId, out var result) ? result : null);
    }

    public bool Split(string sourceTypeId, string targetTypeId, PaneSide side)
    {
        if (string.Equals(sourceTypeId, targetTypeId, StringComparison.Ordinal))
        {
            return false;
        }

        return Apply(root =>
        {
            if (DashboardPaneEditor.Remove(root, sourceTypeId) is not { Status: DashboardRemoveStatus.Removed, Root: { } without })
            {
                return null;
            }

            return DashboardPaneEditor.TrySplit(without, targetTypeId, side, sourceTypeId, out var result) ? result : null;
        });
    }

    public bool Add(string typeId, string targetTypeId, PaneSide side)
    {
        return Apply(root =>
        {
            DashboardLayoutReconciler.AppendMissingTypes(_draft, [typeId]);

            return DashboardPaneEditor.TrySplit(root, targetTypeId, side, typeId, out var result) ? result : null;
        });
    }

    public DashboardRemoveStatus Remove(string typeId)
    {
        var status = DashboardRemoveStatus.Rejected;

        var applied = Apply(root =>
        {
            var removal = DashboardPaneEditor.Remove(root, typeId);

            status = removal.Status;

            return removal.Root;
        });

        if (applied)
        {
            return DashboardRemoveStatus.Removed;
        }

        return status == DashboardRemoveStatus.LastTile ? DashboardRemoveStatus.LastTile : DashboardRemoveStatus.Rejected;
    }

    public bool Undo()
    {
        lock (_gate)
        {
            if (_disposed || _undo.Count == 0)
            {
                return false;
            }

            _draft = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);

            Arm();
        }

        Raise();

        return true;
    }

    public void ResetToDefaults()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Remember(Clone(_draft)!);

            _draft = DashboardLayoutReconciler.ResetToDefaults(DashboardLayoutDefaults.Create(), _draft);

            DashboardLayoutReconciler.SyncRoot(_draft);
            Arm();
        }

        Raise();
    }

    public void Flush()
    {
        var merged = false;

        lock (_gate)
        {
            if (_disposed || !_dirty)
            {
                return;
            }

            _writing = true;

            try
            {
                var result = _coordinator.Commit(_draft, _baseRevision, keepDraftRoot: true);

                _dirty = false;
                _baseRevision = result.Snapshot.Revision;
                merged = result.Merged;

                if (merged && Clone(result.Snapshot.Layout) is { } layout)
                {
                    _draft = layout;
                }
            }
            catch (Exception exception)
            {
                // TODO: отказ записи виден только в журнале; довести его до строки состояния, когда появится второй случай потери правок раскладки
                _logger?.DashboardLayoutSaveFailed(exception);
            }
            finally
            {
                _writing = false;
            }
        }

        if (merged)
        {
            Raise();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        _coordinator.LayoutChanged -= OnLayoutChanged;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Flush();

        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }

    private static DashboardLayoutSettings? Clone(DashboardLayoutSettings? layout)
    {
        return layout is null ? null : JsonStoreClone.DeepClone(layout);
    }

    private static bool Fits(DashboardPane root)
    {
        return DashboardLayoutTree.TryMeasure(root) is { } size
            && size.Columns <= DashboardLayoutDefaults.MaxColumnCount
            && size.Rows <= DashboardLayoutDefaults.MaxRowCount;
    }

    private bool Apply(Func<DashboardPane, DashboardPane?> change)
    {
        lock (_gate)
        {
            if (_disposed || _draft.Root is not { } root)
            {
                return false;
            }

            var previous = Clone(_draft)!;

            if (change(root) is not { } updated || !Fits(updated))
            {
                _draft = previous;

                return false;
            }

            _draft.Root = updated;

            if (!DashboardLayoutReconciler.SyncTiles(_draft))
            {
                _draft = previous;

                return false;
            }

            Remember(previous);
            Arm();
        }

        Raise();

        return true;
    }

    private void Remember(DashboardLayoutSettings previous)
    {
        _undo.Add(previous);

        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }
    }

    private void Arm()
    {
        _dirty = true;
        _timer.Change(WriteDelay, Timeout.InfiniteTimeSpan);
    }

    private void Raise()
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            Changed?.Invoke(this, EventArgs.Empty);

            return;
        }

        _context.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
    }

    private void OnLayoutChanged(object? sender, DashboardLayoutChangedEventArgs e)
    {
        lock (_gate)
        {
            if (_writing || _disposed || e.Snapshot.Revision == _baseRevision || Clone(e.Snapshot.Layout) is not { } layout)
            {
                return;
            }

            _draft = layout;
            _baseRevision = e.Snapshot.Revision;
            _dirty = false;
            _undo.Clear();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        Raise();
    }
}
