using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<string, DashboardTileViewModel> _tilesByTypeId;
    private readonly DashboardLayoutCoordinator _coordinator;
    private readonly TimeProvider _time;
    private readonly ILogger<DashboardEditSession>? _sessionLogger;
    private readonly HashSet<DashboardTileViewModel> _observed = [];
    private DashboardEditSession? _session;
    private bool _suppressCollapsePersist;
    private bool _stacked;

    public DashboardViewModel(
        IEnumerable<DashboardTileViewModel> tiles,
        DashboardLayoutCoordinator coordinator,
        TimeProvider time,
        ILogger<DashboardEditSession>? sessionLogger = null)
    {
        _coordinator = coordinator;
        _time = time;
        _sessionLogger = sessionLogger;
        _tilesByTypeId = new(StringComparer.Ordinal);

        foreach (var tile in tiles)
        {
            _tilesByTypeId[tile.TypeId] = tile;
        }

        Reload();
    }

    public event EventHandler? LayoutChanged;

    public IReadOnlyList<TileBand> Bands { get; private set; } = [];

    public PaneLayout? Pane { get; private set; }

    public bool HasTiles { get; private set; }

    public bool IsEditing => _session is not null;

    public bool CanEdit => Pane is not null && !_stacked;

    public bool CanUndo => _session?.CanUndo == true;

    public IReadOnlyList<HiddenTile> HiddenTiles { get; private set; } = [];

    public void OnEnter()
    {
        if (_session is null)
        {
            Reload();
        }
    }

    public void SetStacked(bool stacked)
    {
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;

        if (stacked)
        {
            StopEditing();
        }

        OnPropertyChanged(nameof(CanEdit));
    }

    public bool Resize(IReadOnlyList<int> path, IReadOnlyList<double> weights)
    {
        return _session?.Resize(path, weights) == true;
    }

    public bool Swap(string firstTypeId, string secondTypeId)
    {
        return _session?.Swap(firstTypeId, secondTypeId) == true;
    }

    public bool Move(string sourceTypeId, string targetTypeId, PaneSide side)
    {
        return _session?.Split(sourceTypeId, targetTypeId, side) == true;
    }

    public void StopEditing()
    {
        if (_session is null)
        {
            return;
        }

        _session.Changed -= OnSessionChanged;
        _session.Dispose();
        _session = null;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(CanUndo));

        Reload();
    }

    public void Reload()
    {
        var layout = _coordinator.Mutate(current =>
        {
            var created = current is null || current.Tiles.Count == 0;
            var target = created ? DashboardLayoutDefaults.Create() : current!;

            return DashboardLayoutReconciler.AppendMissingTypes(target, _tilesByTypeId.Keys) || created ? target : null;
        });

        ApplyLayout(layout ?? DashboardLayoutDefaults.Create());
    }

    [RelayCommand]
    private void ToggleEdit()
    {
        if (_session is not null)
        {
            StopEditing();

            return;
        }

        if (!CanEdit)
        {
            return;
        }

        _session = new(_coordinator, _time, _sessionLogger);
        _session.Changed += OnSessionChanged;

        OnPropertyChanged(nameof(IsEditing));

        ApplySession();
    }

    [RelayCommand]
    private void Undo()
    {
        _session?.Undo();
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _session?.ResetToDefaults();
    }

    [RelayCommand]
    private void AddTile(string? typeId)
    {
        if (_session is null || string.IsNullOrEmpty(typeId) || LargestLeaf() is not { } target)
        {
            return;
        }

        _session.Add(typeId, target, PaneSide.Right);
    }

    [RelayCommand]
    private void RemoveTile(string? typeId)
    {
        if (_session is not null && !string.IsNullOrEmpty(typeId))
        {
            _session.Remove(typeId);
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;

        foreach (var tile in _observed)
        {
            tile.PropertyChanged -= OnTilePropertyChanged;
        }

        _observed.Clear();
    }

    private static int? ResolveMaxSize(int? overrideValue, int? typeDefault)
    {
        return overrideValue switch
        {
            null => typeDefault,
            <= 0 => null,
            _ => overrideValue,
        };
    }

    private static bool Stretches(Placement placement)
    {
        return placement.Tile.FillsAvailableSpace && !placement.IsCollapsed;
    }

    private static bool Grows(Placement placement)
    {
        return placement.Tile.GrowsWithSpace && !placement.IsCollapsed;
    }

    private static PaneLayout? BuildPane(DashboardPane pane, IReadOnlyDictionary<string, Placement> placements, int[] path)
    {
        return pane switch
        {
            TilePane tile => placements.TryGetValue(tile.TypeId, out var placement) ? BuildLeaf(placement, path) : null,
            SplitPane { Orientation: SplitOrientation.Columns or SplitOrientation.Rows, Children.Count: > 0 } split => BuildSplit(split, placements, path),
            _ => null,
        };
    }

    private static PaneLayout BuildLeaf(Placement placement, int[] path)
    {
        var star = new GridLength(1, GridUnitType.Star);

        if (Stretches(placement))
        {
            return new TilePaneLayout(placement.Tile, new(star, double.PositiveInfinity), new(star, double.PositiveInfinity), path);
        }

        var width = new TrackSize(GridLength.Auto, placement.MaxWidth ?? double.PositiveInfinity);
        var height = new TrackSize(Grows(placement) ? star : GridLength.Auto, placement.MaxHeight ?? double.PositiveInfinity);

        return new TilePaneLayout(placement.Tile, width, height, path);
    }

    private static PaneLayout? BuildSplit(SplitPane split, IReadOnlyDictionary<string, Placement> placements, int[] path)
    {
        if (DashboardLayoutTree.ResolveWeights(split.Children) is not { } weights)
        {
            return null;
        }

        var children = new List<PaneLayoutSlot>(split.Children.Count);

        for (var index = 0; index < split.Children.Count; index++)
        {
            var slot = split.Children[index];

            if (BuildPane(slot.Pane, placements, [.. path, index]) is { } child)
            {
                children.Add(new(child, weights[index], HasWeight(slot, placements)));
            }
        }

        if (children.Count == 0)
        {
            return null;
        }

        if (children.Count == 1)
        {
            return children[0].Pane;
        }

        var alongColumns = split.Orientation == SplitOrientation.Columns;
        var authored = children.Exists(child => child.HasWeight);

        var width = MergeTracks(children, static pane => pane.Width, alongColumns, alongColumns && authored);
        var height = MergeTracks(children, static pane => pane.Height, !alongColumns, !alongColumns && authored);

        return new SplitPaneLayout(split.Orientation, children, width, height, path, children.Count == split.Children.Count);
    }

    private static bool HasWeight(PaneSlot slot, IReadOnlyDictionary<string, Placement> placements)
    {
        if (slot.Weight is not { } weight || weight <= 0 || !double.IsFinite(weight))
        {
            return false;
        }

        return slot.Pane is not TilePane tile
            || !placements.TryGetValue(tile.TypeId, out var placement)
            || !placement.IsCollapsed;
    }

    private static TrackSize MergeTracks(IReadOnlyList<PaneLayoutSlot> children, Func<PaneLayout, TrackSize> axis, bool alongSplit, bool authored)
    {
        var tracks = children.Select(child => axis(child.Pane)).ToList();
        var ceiling = alongSplit ? tracks.Sum(track => track.Max) : tracks.Max(track => track.Max);

        return new(
            authored || tracks.Exists(track => track.Length.IsStar) ? new(1, GridUnitType.Star) : GridLength.Auto,
            double.IsInfinity(ceiling) ? double.PositiveInfinity : ceiling);
    }

    private static IReadOnlyList<TileBand> BuildBands(IReadOnlyList<Placement> placements, int columnCount, int rowCount)
    {
        var bands = new List<TileBand>();

        foreach (var (start, end) in SplitColumns(placements, columnCount))
        {
            var members = placements
                .Where(p => p.Column >= start && p.Column + p.ColumnSpan <= end)
                .ToList();

            if (members.Count == 0)
            {
                continue;
            }

            var columns = new TrackSize[end - start];

            for (var column = start; column < end; column++)
            {
                columns[column - start] = ComputeColumn(members, column);
            }

            var rows = new TrackSize[rowCount];

            for (var row = 0; row < rowCount; row++)
            {
                rows[row] = ComputeRow(members, row);
            }

            var slots = members
                .Select(p => new TileSlot(p.Tile, p.Row, p.Column - start, p.RowSpan, p.ColumnSpan))
                .ToList();

            bands.Add(new(ComputeBandWidth(members, columns), columns, rows, slots));
        }

        return bands;
    }

    private static IEnumerable<(int Start, int End)> SplitColumns(IReadOnlyList<Placement> placements, int columnCount)
    {
        var start = 0;

        for (var seam = 1; seam < columnCount; seam++)
        {
            if (placements.Any(p => p.Column < seam && seam < p.Column + p.ColumnSpan))
            {
                continue;
            }

            yield return (start, seam);
            start = seam;
        }

        yield return (start, columnCount);
    }

    private static TrackSize ComputeColumn(IReadOnlyList<Placement> members, int column)
    {
        var covering = members
            .Where(p => p.Column <= column && column < p.Column + p.ColumnSpan)
            .ToList();

        if (covering.Any(Stretches))
        {
            return new(new(1, GridUnitType.Star), double.PositiveInfinity);
        }

        return new(GridLength.Auto, Ceiling(covering, p => p.MaxWidth, p => p.ColumnSpan));
    }

    private static TrackSize ComputeRow(IReadOnlyList<Placement> members, int row)
    {
        var covering = members
            .Where(p => p.Row <= row && row < p.Row + p.RowSpan)
            .ToList();

        if (covering.Any(Stretches))
        {
            return new(new(1, GridUnitType.Star), double.PositiveInfinity);
        }

        var ceiling = Ceiling(covering, p => p.MaxHeight, p => p.RowSpan);

        return covering.Any(Grows)
            ? new(new(1, GridUnitType.Star), ceiling)
            : new(GridLength.Auto, ceiling);
    }

    private static TrackSize ComputeBandWidth(IReadOnlyList<Placement> members, IReadOnlyList<TrackSize> columns)
    {
        if (members.Any(Stretches))
        {
            return new(new(1, GridUnitType.Star), double.PositiveInfinity);
        }

        var ceiling = columns.Sum(c => c.Max);

        return new(GridLength.Auto, double.IsInfinity(ceiling) ? double.PositiveInfinity : ceiling);
    }

    private static double Ceiling(IReadOnlyList<Placement> covering, Func<Placement, int?> size, Func<Placement, int> span)
    {
        if (covering.Count == 0 || covering.Any(p => size(p) is null))
        {
            return double.PositiveInfinity;
        }

        return covering.Max(p => size(p)!.Value / (double)span(p));
    }

    private void ApplyLayout(DashboardLayoutSettings layout)
    {
        var columnCount = Math.Clamp(layout.ColumnCount, DashboardLayoutDefaults.MinColumnCount, DashboardLayoutDefaults.MaxColumnCount);
        var rowCount = Math.Clamp(layout.RowCount, DashboardLayoutDefaults.MinRowCount, DashboardLayoutDefaults.MaxRowCount);

        var placements = BuildPlacements(layout, columnCount, rowCount);

        ObserveCollapse(placements);

        layout.ColumnCount = columnCount;
        layout.RowCount = rowCount;

        DashboardLayoutReconciler.SyncRoot(layout);

        Pane = layout.Root is null
            ? null
            : BuildPane(layout.Root, placements.ToDictionary(placement => placement.Tile.TypeId, StringComparer.Ordinal), []);

        Bands = Pane is null ? BuildBands(placements, columnCount, rowCount) : [];
        HasTiles = placements.Count > 0;

        HiddenTiles = layout.Tiles
            .Where(tile => !tile.IsVisible && _tilesByTypeId.ContainsKey(tile.TypeId))
            .Select(tile => new HiddenTile(tile.TypeId, _tilesByTypeId[tile.TypeId].Title))
            .ToList();

        OnPropertyChanged(nameof(HasTiles));
        OnPropertyChanged(nameof(HiddenTiles));
        OnPropertyChanged(nameof(CanEdit));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<Placement> BuildPlacements(DashboardLayoutSettings layout, int columnCount, int rowCount)
    {
        var placements = new List<Placement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tile in layout.Tiles.Where(t => t.IsVisible))
        {
            if (!seen.Add(tile.TypeId) || !_tilesByTypeId.TryGetValue(tile.TypeId, out var vm))
            {
                continue;
            }

            var row = Math.Clamp(tile.Row, 0, rowCount - 1);
            var column = Math.Clamp(tile.Column, 0, columnCount - 1);
            var columnSpan = Math.Clamp(tile.ColumnSpan, 1, columnCount - column);
            var rowSpan = Math.Clamp(tile.RowSpan, 1, rowCount - row);

            placements.Add(new(vm, row, column, columnSpan, rowSpan,
                ResolveMaxSize(tile.MaxWidth, vm.MaxWidth),
                ResolveMaxSize(tile.MaxHeight, vm.MaxHeight),
                !IsEditing && tile.IsCollapsed));
        }

        return placements;
    }

    private void ObserveCollapse(IReadOnlyList<Placement> placements)
    {
        foreach (var tile in _observed)
        {
            tile.PropertyChanged -= OnTilePropertyChanged;
        }

        _observed.Clear();

        _suppressCollapsePersist = true;

        try
        {
            foreach (var placement in placements)
            {
                placement.Tile.IsCollapsed = placement.IsCollapsed;

                if (_observed.Add(placement.Tile))
                {
                    placement.Tile.PropertyChanged += OnTilePropertyChanged;
                }
            }
        }
        finally
        {
            _suppressCollapsePersist = false;
        }
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressCollapsePersist || sender is not DashboardTileViewModel tile)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(DashboardTileViewModel.GrowsWithSpace), StringComparison.Ordinal))
        {
            Reload();
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(DashboardTileViewModel.IsCollapsed), StringComparison.Ordinal))
        {
            return;
        }

        var persisted = false;

        var layout = _coordinator.Mutate(current =>
        {
            var setting = current?.Tiles.FirstOrDefault(t => string.Equals(t.TypeId, tile.TypeId, StringComparison.Ordinal));

            if (setting is null)
            {
                return null;
            }

            setting.IsCollapsed = tile.IsCollapsed;
            persisted = true;

            return current;
        });

        if (persisted && layout is not null)
        {
            ApplyLayout(layout);
        }
    }

    private void ApplySession()
    {
        if (_session is { } session)
        {
            ApplyLayout(session.Draft);
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        ApplySession();

        OnPropertyChanged(nameof(CanUndo));
    }

    private string? LargestLeaf()
    {
        return _session?.Draft.Tiles
            .Where(tile => tile.IsVisible)
            .OrderByDescending(tile => tile.RowSpan * tile.ColumnSpan)
            .Select(tile => tile.TypeId)
            .FirstOrDefault();
    }

    private sealed record Placement(
        DashboardTileViewModel Tile,
        int Row,
        int Column,
        int ColumnSpan,
        int RowSpan,
        int? MaxWidth,
        int? MaxHeight,
        bool IsCollapsed);
}

public sealed record HiddenTile(string TypeId, string Title);

public sealed record TrackSize(GridLength Length, double Max);

public sealed record TileSlot(
    DashboardTileViewModel Tile,
    int Row,
    int Column,
    int RowSpan,
    int ColumnSpan);

public sealed record TileBand(
    TrackSize Width,
    IReadOnlyList<TrackSize> Columns,
    IReadOnlyList<TrackSize> Rows,
    IReadOnlyList<TileSlot> Tiles);

public abstract record PaneLayout(TrackSize Width, TrackSize Height, int[] Path)
{
    public bool Scrollable => Width.Length.IsAuto && Height.Length.IsAuto;

    public double MinWidth(double leafMinimum)
    {
        return this switch
        {
            SplitPaneLayout { Orientation: SplitOrientation.Columns } split => split.Children.Sum(child => child.Pane.MinWidth(leafMinimum)),
            SplitPaneLayout split => split.Children.Max(child => child.Pane.MinWidth(leafMinimum)),
            _ => Width.Length.IsStar ? leafMinimum : 0,
        };
    }
}

public sealed record TilePaneLayout(DashboardTileViewModel Tile, TrackSize Width, TrackSize Height, int[] Path)
    : PaneLayout(Width, Height, Path);

public sealed record SplitPaneLayout(
    SplitOrientation Orientation,
    IReadOnlyList<PaneLayoutSlot> Children,
    TrackSize Width,
    TrackSize Height,
    int[] Path,
    bool IsComplete) : PaneLayout(Width, Height, Path);

public sealed record PaneLayoutSlot(PaneLayout Pane, double Weight, bool HasWeight);
