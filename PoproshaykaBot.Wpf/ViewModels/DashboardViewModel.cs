using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<string, DashboardTileViewModel> _tilesByTypeId;
    private readonly DashboardLayoutStore _layoutStore;
    private readonly HashSet<DashboardTileViewModel> _observed = [];
    private bool _suppressCollapsePersist;

    public DashboardViewModel(IEnumerable<DashboardTileViewModel> tiles, DashboardLayoutStore layoutStore)
    {
        _layoutStore = layoutStore;
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

    public void OnEnter()
    {
        Reload();
    }

    public void Reload()
    {
        var layout = _layoutStore.LoadDashboard();
        var created = layout is null || layout.Tiles.Count == 0;

        if (created)
        {
            layout = DashboardLayoutDefaults.Create();
        }

        if (DashboardLayoutReconciler.AppendMissingTypes(layout!, _tilesByTypeId.Keys) || created)
        {
            _layoutStore.SaveDashboard(layout!);
        }

        ApplyLayout(layout!);
    }

    public void Dispose()
    {
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

    private static PaneLayout? BuildPane(DashboardPane pane, IReadOnlyDictionary<string, Placement> placements)
    {
        return pane switch
        {
            TilePane tile => placements.TryGetValue(tile.TypeId, out var placement) ? BuildLeaf(placement) : null,
            SplitPane { Orientation: SplitOrientation.Columns or SplitOrientation.Rows, Children.Count: > 0 } split => BuildSplit(split, placements),
            _ => null,
        };
    }

    private static PaneLayout BuildLeaf(Placement placement)
    {
        var star = new GridLength(1, GridUnitType.Star);

        if (Stretches(placement))
        {
            return new TilePaneLayout(placement.Tile, new(star, double.PositiveInfinity), new(star, double.PositiveInfinity));
        }

        var width = new TrackSize(GridLength.Auto, placement.MaxWidth ?? double.PositiveInfinity);
        var height = new TrackSize(Grows(placement) ? star : GridLength.Auto, placement.MaxHeight ?? double.PositiveInfinity);

        return new TilePaneLayout(placement.Tile, width, height);
    }

    private static PaneLayout? BuildSplit(SplitPane split, IReadOnlyDictionary<string, Placement> placements)
    {
        if (DashboardLayoutTree.ResolveWeights(split.Children) is not { } weights)
        {
            return null;
        }

        var children = new List<PaneLayoutSlot>(split.Children.Count);

        for (var index = 0; index < split.Children.Count; index++)
        {
            if (BuildPane(split.Children[index].Pane, placements) is { } child)
            {
                children.Add(new(child, weights[index]));
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

        var width = MergeTracks(children, static pane => pane.Width, alongColumns);
        var height = MergeTracks(children, static pane => pane.Height, !alongColumns);

        return new SplitPaneLayout(split.Orientation, children, width, height);
    }

    private static TrackSize MergeTracks(IReadOnlyList<PaneLayoutSlot> children, Func<PaneLayout, TrackSize> axis, bool alongSplit)
    {
        var tracks = children.Select(child => axis(child.Pane)).ToList();
        var ceiling = alongSplit ? tracks.Sum(track => track.Max) : tracks.Max(track => track.Max);

        return new(
            tracks.Exists(track => track.Length.IsStar) ? new(1, GridUnitType.Star) : GridLength.Auto,
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
            : BuildPane(layout.Root, placements.ToDictionary(placement => placement.Tile.TypeId, StringComparer.Ordinal));

        Bands = Pane is null ? BuildBands(placements, columnCount, rowCount) : [];
        HasTiles = placements.Count > 0;

        OnPropertyChanged(nameof(HasTiles));
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
                tile.IsCollapsed));
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

        var layout = _layoutStore.LoadDashboard();

        var setting = layout?.Tiles.FirstOrDefault(t => string.Equals(t.TypeId, tile.TypeId, StringComparison.Ordinal));

        if (setting is null)
        {
            return;
        }

        setting.IsCollapsed = tile.IsCollapsed;
        _layoutStore.SaveDashboard(layout!);

        ApplyLayout(layout!);
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

public abstract record PaneLayout(TrackSize Width, TrackSize Height)
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

public sealed record TilePaneLayout(DashboardTileViewModel Tile, TrackSize Width, TrackSize Height)
    : PaneLayout(Width, Height);

public sealed record SplitPaneLayout(
    SplitOrientation Orientation,
    IReadOnlyList<PaneLayoutSlot> Children,
    TrackSize Width,
    TrackSize Height) : PaneLayout(Width, Height);

public sealed record PaneLayoutSlot(PaneLayout Pane, double Weight);
