using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private const double CollapsedRowHeight = 44;
    private const double CollapsedColumnWidth = 56;

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

    public IReadOnlyList<TilePlacement> Placements { get; private set; } = [];

    public IReadOnlyList<GridLength> ColumnWidths { get; private set; } = [];

    public IReadOnlyList<GridLength> RowHeights { get; private set; } = [];

    public bool HasTiles => Placements.Count > 0;

    public void OnEnter()
    {
        Reload();
    }

    public void Reload()
    {
        var layout = _layoutStore.LoadDashboard();

        if (layout is null || layout.Tiles.Count == 0)
        {
            layout = DashboardLayoutDefaults.Create();
            _layoutStore.SaveDashboard(layout);
        }

        ApplyLayout(layout);
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

    private void ApplyLayout(DashboardLayoutSettings layout)
    {
        var columnCount = Math.Clamp(layout.ColumnCount, DashboardLayoutDefaults.MinColumnCount, DashboardLayoutDefaults.MaxColumnCount);
        var rowCount = Math.Clamp(layout.RowCount, DashboardLayoutDefaults.MinRowCount, DashboardLayoutDefaults.MaxRowCount);

        var placements = BuildPlacements(layout, columnCount, rowCount);

        ColumnWidths = ComputeColumnWidths(placements, columnCount, rowCount);
        RowHeights = ComputeRowHeights(placements, rowCount);

        ObserveCollapse(placements);

        Placements = placements
            .Select(p => new TilePlacement(p.Tile, p.Row, p.Column, p.ColumnSpan, p.RowSpan))
            .ToList();

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

    private IReadOnlyList<GridLength> ComputeColumnWidths(IReadOnlyList<Placement> placements, int columnCount, int rowCount)
    {
        var widths = new GridLength[columnCount];

        for (var column = 0; column < columnCount; column++)
        {
            var columnIndex = column;

            var fullColumnTile = placements.FirstOrDefault(p => p.Row == 0
                                                                && p.RowSpan == rowCount
                                                                && p.Column <= columnIndex
                                                                && columnIndex < p.Column + p.ColumnSpan);

            if (fullColumnTile is { IsCollapsed: true })
            {
                widths[column] = new(CollapsedColumnWidth);
                continue;
            }

            if (fullColumnTile != null)
            {
                widths[column] = new(1, GridUnitType.Star);
                continue;
            }

            var singleColumnTiles = placements
                .Where(p => p.ColumnSpan == 1 && p.Column == columnIndex)
                .ToList();

            if (singleColumnTiles.Any(p => p.MaxWidth == null))
            {
                widths[column] = new(1, GridUnitType.Star);
                continue;
            }

            var compact = singleColumnTiles.Where(p => p.MaxWidth.HasValue).ToList();

            widths[column] = compact.Count > 0
                ? new GridLength(compact.Max(p => p.MaxWidth!.Value))
                : new(1, GridUnitType.Star);
        }

        return widths;
    }

    private IReadOnlyList<GridLength> ComputeRowHeights(IReadOnlyList<Placement> placements, int rowCount)
    {
        var heights = new GridLength[rowCount];

        for (var row = 0; row < rowCount; row++)
        {
            var rowIndex = row;
            var singleRowTiles = placements
                .Where(p => p.RowSpan == 1 && p.Row == rowIndex)
                .ToList();

            if (singleRowTiles.Count > 0 && singleRowTiles.All(p => p.IsCollapsed))
            {
                heights[row] = new(CollapsedRowHeight);
                continue;
            }

            var nonCollapsed = singleRowTiles.Where(p => !p.IsCollapsed).ToList();

            if (nonCollapsed.Any(p => p.MaxHeight == null))
            {
                heights[row] = new(1, GridUnitType.Star);
                continue;
            }

            var compact = nonCollapsed.Where(p => p.MaxHeight.HasValue).ToList();

            heights[row] = compact.Count > 0
                ? new GridLength(compact.Max(p => p.MaxHeight!.Value))
                : new(1, GridUnitType.Star);
        }

        return heights;
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
        if (_suppressCollapsePersist
            || !string.Equals(e.PropertyName, nameof(DashboardTileViewModel.IsCollapsed), StringComparison.Ordinal)
            || sender is not DashboardTileViewModel tile)
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

public sealed record TilePlacement(
    DashboardTileViewModel Tile,
    int Row,
    int Column,
    int ColumnSpan,
    int RowSpan);
