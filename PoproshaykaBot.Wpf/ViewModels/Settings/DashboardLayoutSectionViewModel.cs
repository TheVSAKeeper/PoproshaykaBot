using PoproshaykaBot.Core.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class DashboardLayoutSectionViewModel : ObservableObject
{
    private readonly Dictionary<string, TileMeta> _catalog;
    private readonly Dictionary<string, PlacedTile> _placed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<DashboardTileSettings> _preserved = [];
    private bool _suppress;

    [ObservableProperty]
    private int _columnCount = DashboardLayoutDefaults.DefaultColumnCount;

    [ObservableProperty]
    private int _rowCount = DashboardLayoutDefaults.DefaultRowCount;

    public DashboardLayoutSectionViewModel(IEnumerable<DashboardTileViewModel> tiles)
    {
        _catalog = tiles.ToDictionary(
            tile => tile.TypeId,
            tile => new TileMeta(tile.TypeId, tile.Title, tile.MaxWidth, tile.MaxHeight),
            StringComparer.Ordinal);
    }

    public event EventHandler? LayoutChanged;

    public event EventHandler? Edited;

    public int MinColumnCount => DashboardLayoutDefaults.MinColumnCount;

    public int MaxColumnCount => DashboardLayoutDefaults.MaxColumnCount;

    public int MinRowCount => DashboardLayoutDefaults.MinRowCount;

    public int MaxRowCount => DashboardLayoutDefaults.MaxRowCount;

    public IReadOnlyList<int> MaxWidthPresets { get; } = [200, 300, 400, 500, 600, 800];

    public IReadOnlyList<int> MaxHeightPresets { get; } = [100, 150, 200, 250, 300, 400, 500];

    public IReadOnlyCollection<PlacedTile> PlacedTiles => _placed.Values;

    public IReadOnlyList<TileMeta> AvailablePalette =>
        _catalog.Values.Where(meta => !_placed.ContainsKey(meta.TypeId)).ToList();

    public TileMeta? Meta(string typeId)
    {
        return _catalog.GetValueOrDefault(typeId);
    }

    public PlacedTile? Placed(string typeId)
    {
        return _placed.GetValueOrDefault(typeId);
    }

    public void LoadSettings(DashboardLayoutSettings? draft)
    {
        _preserved.Clear();

        if (draft is not { Tiles.Count: > 0 })
        {
            LoadFrom(DashboardLayoutDefaults.Create());
            return;
        }

        _preserved.AddRange(draft.Tiles.Where(tile => !tile.IsVisible || !_catalog.ContainsKey(tile.TypeId)));

        LoadFrom(draft);
    }

    public DashboardLayoutSettings BuildLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = ColumnCount,
            RowCount = RowCount,
        };

        var order = 0;

        foreach (var placed in _placed.Values.OrderBy(tile => tile.Row).ThenBy(tile => tile.Column))
        {
            layout.Tiles.Add(new()
            {
                Id = placed.TypeId,
                TypeId = placed.TypeId,
                Order = order++,
                Row = placed.Row,
                Column = placed.Column,
                ColumnSpan = placed.ColumnSpan,
                RowSpan = placed.RowSpan,
                IsVisible = true,
                IsCollapsed = _collapsed.Contains(placed.TypeId),
                MaxHeight = placed.MaxHeight,
                MaxWidth = placed.MaxWidth,
            });
        }

        foreach (var tile in _preserved.Where(tile => !_placed.ContainsKey(tile.TypeId)))
        {
            tile.Order = order++;
            layout.Tiles.Add(tile);
        }

        return layout;
    }

    public void PlaceOrMove(string typeId, int row, int column)
    {
        if (!_catalog.ContainsKey(typeId) || row < 0 || row >= RowCount || column < 0 || column >= ColumnCount)
        {
            return;
        }

        if (!_placed.TryGetValue(typeId, out var placed))
        {
            placed = new() { TypeId = typeId };
            _placed[typeId] = placed;
        }

        placed.Row = row;
        placed.Column = column;
        DashboardLayoutCalculator.ClampPlacement(placed, ColumnCount, RowCount);

        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    public void RemoveTile(string typeId)
    {
        if (!_placed.Remove(typeId))
        {
            return;
        }

        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    public void SetColumnSpan(string typeId, int span)
    {
        if (!_placed.TryGetValue(typeId, out var placed))
        {
            return;
        }

        placed.ColumnSpan = span;
        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    public void SetRowSpan(string typeId, int span)
    {
        if (!_placed.TryGetValue(typeId, out var placed))
        {
            return;
        }

        placed.RowSpan = span;
        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    public void SetMaxWidth(string typeId, int? value)
    {
        if (!_placed.TryGetValue(typeId, out var placed))
        {
            return;
        }

        placed.MaxWidth = value;
        RaiseEdited();
    }

    public void SetMaxHeight(string typeId, int? value)
    {
        if (!_placed.TryGetValue(typeId, out var placed))
        {
            return;
        }

        placed.MaxHeight = value;
        RaiseEdited();
    }

    [RelayCommand]
    private void ResetLayout()
    {
        LoadFrom(DashboardLayoutDefaults.Create());
        RaiseEdited();
    }

    [RelayCommand]
    private void ClearGrid()
    {
        _placed.Clear();
        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    partial void OnColumnCountChanged(int value)
    {
        OnGridSizeChanged();
    }

    partial void OnRowCountChanged(int value)
    {
        OnGridSizeChanged();
    }

    private void OnGridSizeChanged()
    {
        if (_suppress)
        {
            return;
        }

        Resolve();
        RaiseLayoutChanged();
        RaiseEdited();
    }

    private void LoadFrom(DashboardLayoutSettings layout)
    {
        _suppress = true;

        try
        {
            ColumnCount = Math.Clamp(layout.ColumnCount, MinColumnCount, MaxColumnCount);
            RowCount = Math.Clamp(layout.RowCount, MinRowCount, MaxRowCount);

            _placed.Clear();
            _collapsed.Clear();

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tile in layout.Tiles.Where(tile => tile.IsVisible))
            {
                if (!_catalog.ContainsKey(tile.TypeId) || !seen.Add(tile.TypeId))
                {
                    continue;
                }

                var placed = new PlacedTile
                {
                    TypeId = tile.TypeId,
                    Row = tile.Row,
                    Column = tile.Column,
                    ColumnSpan = tile.ColumnSpan,
                    RowSpan = tile.RowSpan,
                    MaxHeight = tile.MaxHeight,
                    MaxWidth = tile.MaxWidth,
                };

                DashboardLayoutCalculator.ClampPlacement(placed, ColumnCount, RowCount);
                _placed[tile.TypeId] = placed;

                if (tile.IsCollapsed)
                {
                    _collapsed.Add(tile.TypeId);
                }
            }
        }
        finally
        {
            _suppress = false;
        }

        Resolve();
        RaiseLayoutChanged();
    }

    private void Resolve()
    {
        var (_, unplaceable) = DashboardLayoutCalculator.ResolveLayout(_placed.Values, RowCount, ColumnCount);

        foreach (var lost in unplaceable)
        {
            _placed.Remove(lost.TypeId);
        }

        OnPropertyChanged(nameof(AvailablePalette));
        OnPropertyChanged(nameof(PlacedTiles));
    }

    private void RaiseLayoutChanged()
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseEdited()
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record TileMeta(string TypeId, string Title, int? DefaultMaxWidth, int? DefaultMaxHeight);
