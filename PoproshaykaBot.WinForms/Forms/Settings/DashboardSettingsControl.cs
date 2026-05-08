using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;
using System.ComponentModel;

namespace PoproshaykaBot.WinForms.Forms.Settings;

public sealed partial class DashboardSettingsControl : UserControl, IDashboardTileCommands
{
    private readonly Dictionary<DashboardTileType, Button> _paletteCards = [];
    private readonly Dictionary<DashboardTileType, PlacedTile> _placedTiles = [];
    private readonly Dictionary<DashboardTileType, Panel> _tilePanels = [];
    private readonly DashboardTileContextMenuBuilder _contextMenuBuilder = new();
    private DashboardTileDragDropController? _dragDrop;
    private DashboardLayoutSettings? _pendingLayout;
    private bool _initialized;
    private bool _suppressEvents;

    public DashboardSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public IDashboardTileCatalog TileCatalog { get; internal init; } = null!;

    public DashboardLayoutSettings? CurrentLayout { get; private set; }

    public void LoadSettings(DashboardLayoutSettings? draft)
    {
        var layout = draft is { Tiles.Count: > 0 }
            ? draft
            : DashboardLayoutDefaults.Create();

        CurrentLayout = layout;

        if (_initialized)
        {
            ApplyLayout(layout);
        }
        else
        {
            _pendingLayout = layout;
        }
    }

    public DashboardLayoutSettings SaveSettings()
    {
        if (!_initialized)
        {
            return _pendingLayout ?? DashboardLayoutDefaults.Create();
        }

        var layout = new DashboardLayoutSettings
        {
            ColumnCount = (int)_gridColumnsNumeric.Value,
            RowCount = (int)_gridRowsNumeric.Value,
        };

        var collapsedByType = CurrentLayout?.Tiles
                                  .Where(t => t.IsCollapsed)
                                  .Select(t => t.TypeId)
                                  .ToHashSet(StringComparer.Ordinal)
                              ?? [];

        var order = 0;

        foreach (var (type, placed) in _placedTiles
                     .OrderBy(kv => kv.Value.Row)
                     .ThenBy(kv => kv.Value.Column))
        {
            layout.Tiles.Add(new()
            {
                Id = type.Id,
                TypeId = type.Id,
                Order = order++,
                Row = placed.Row,
                Column = placed.Column,
                ColumnSpan = placed.ColumnSpan,
                RowSpan = placed.RowSpan,
                IsVisible = true,
                IsCollapsed = collapsedByType.Contains(type.Id),
                MaxHeight = placed.MaxHeight,
                MaxWidth = placed.MaxWidth,
            });
        }

        CurrentLayout = layout;
        return layout;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        _dragDrop = new(_placedTiles, PlaceOrMoveTile, ShowTileContextMenu);

        BuildPalette();
        ApplyLayout(_pendingLayout ?? DashboardLayoutDefaults.Create());
        _pendingLayout = null;
    }

    private void OnTileContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_tileContextMenu.Tag is not DashboardTileType type || !_placedTiles.TryGetValue(type, out var placed))
        {
            e.Cancel = true;
            return;
        }

        _contextMenuBuilder.Populate(_tileContextMenu,
            type,
            placed,
            (int)_gridColumnsNumeric.Value,
            (int)_gridRowsNumeric.Value,
            this,
            FindForm() ?? (IWin32Window)this);
    }

    private void OnGridColumnsValueChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RebuildGrid();
        OnChanged();
    }

    private void OnGridRowsValueChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RebuildGrid();
        OnChanged();
    }

    private void OnResetLayoutButtonClicked(object? sender, EventArgs e)
    {
        ApplyLayout(DashboardLayoutDefaults.Create());
        OnChanged();
    }

    private void OnClearGridButtonClicked(object? sender, EventArgs e)
    {
        _placedTiles.Clear();
        RebuildGrid();
        OnChanged();
    }

    void IDashboardTileCommands.SetColumnSpan(DashboardTileType type, int span)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.ColumnSpan = span;
        RebuildGrid();
        OnChanged();
    }

    void IDashboardTileCommands.SetRowSpan(DashboardTileType type, int span)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.RowSpan = span;
        RebuildGrid();
        OnChanged();
    }

    void IDashboardTileCommands.SetMaxWidth(DashboardTileType type, int? value)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.MaxWidth = value;
        OnChanged();
    }

    void IDashboardTileCommands.SetMaxHeight(DashboardTileType type, int? value)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.MaxHeight = value;
        OnChanged();
    }

    void IDashboardTileCommands.RemoveTile(DashboardTileType type)
    {
        if (!_placedTiles.Remove(type))
        {
            return;
        }

        RebuildGrid();
        OnChanged();
    }

    private void ShowTileContextMenu(DashboardTileType type, Control anchor, Point location)
    {
        _tileContextMenu.Tag = type;
        _tileContextMenu.Show(anchor, location);
    }

    private void BuildPalette()
    {
        _paletteFlowLayoutPanel.SuspendLayout();

        try
        {
            foreach (var type in TileCatalog.All)
            {
                var button = new Button
                {
                    Tag = type,
                    Text = type.Title,
                    AutoSize = false,
                    Width = LogicalToDeviceUnits(170),
                    Height = LogicalToDeviceUnits(40),
                    Margin = new(0, 0, 0, LogicalToDeviceUnits(6)),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new(LogicalToDeviceUnits(8), 0, 0, 0),
                    UseVisualStyleBackColor = true,
                };

                button.MouseDown += _dragDrop!.HandlePaletteMouseDown;
                _paletteCards[type] = button;
                _paletteFlowLayoutPanel.Controls.Add(button);
            }
        }
        finally
        {
            _paletteFlowLayoutPanel.ResumeLayout();
        }
    }

    private void ApplyLayout(DashboardLayoutSettings layout)
    {
        var columnCount = Math.Clamp(layout.ColumnCount, DashboardLayoutDefaults.MinColumnCount, DashboardLayoutDefaults.MaxColumnCount);
        var rowCount = Math.Clamp(layout.RowCount, DashboardLayoutDefaults.MinRowCount, DashboardLayoutDefaults.MaxRowCount);

        _suppressEvents = true;

        try
        {
            _gridColumnsNumeric.Value = columnCount;
            _gridRowsNumeric.Value = rowCount;
        }
        finally
        {
            _suppressEvents = false;
        }

        _placedTiles.Clear();

        var seen = new HashSet<DashboardTileType>();

        foreach (var tile in layout.Tiles.Where(t => t.IsVisible))
        {
            var type = TileCatalog.Find(tile.TypeId);

            if (type == null || !seen.Add(type))
            {
                continue;
            }

            var placed = new PlacedTile
            {
                Row = tile.Row,
                Column = tile.Column,
                ColumnSpan = tile.ColumnSpan,
                RowSpan = tile.RowSpan,
                MaxHeight = tile.MaxHeight,
                MaxWidth = tile.MaxWidth,
            };

            DashboardLayoutCalculator.ClampPlacement(placed, columnCount, rowCount);
            _placedTiles[type] = placed;
        }

        RebuildGrid();
    }

    private void RebuildGrid()
    {
        var columnCount = (int)_gridColumnsNumeric.Value;
        var rowCount = (int)_gridRowsNumeric.Value;

        _gridLayoutPanel.SuspendLayout();

        try
        {
            DisposeGridControls();

            _gridLayoutPanel.RowStyles.Clear();
            _gridLayoutPanel.ColumnStyles.Clear();
            _gridLayoutPanel.ColumnCount = columnCount;
            _gridLayoutPanel.RowCount = rowCount;

            for (var c = 0; c < columnCount; c++)
            {
                _gridLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F / columnCount));
            }

            for (var r = 0; r < rowCount; r++)
            {
                _gridLayoutPanel.RowStyles.Add(new(SizeType.Percent, 100F / rowCount));
            }

            foreach (var (_, placed) in _placedTiles)
            {
                DashboardLayoutCalculator.ClampPlacement(placed, columnCount, rowCount);
            }

            var occupied = DashboardLayoutCalculator.BuildOccupancyMap(_placedTiles.Values, rowCount, columnCount);

            foreach (var (type, placed) in _placedTiles)
            {
                var panel = CreateTilePanel(type);
                _gridLayoutPanel.Controls.Add(panel, placed.Column, placed.Row);
                _gridLayoutPanel.SetColumnSpan(panel, placed.ColumnSpan);
                _gridLayoutPanel.SetRowSpan(panel, placed.RowSpan);
                _tilePanels[type] = panel;
            }

            for (var r = 0; r < rowCount; r++)
            {
                for (var c = 0; c < columnCount; c++)
                {
                    if (occupied[r, c])
                    {
                        continue;
                    }

                    var placeholder = CreatePlaceholderPanel(r, c);
                    _gridLayoutPanel.Controls.Add(placeholder, c, r);
                }
            }
        }
        finally
        {
            _gridLayoutPanel.ResumeLayout();
        }

        UpdatePaletteEnabled();
    }

    private void DisposeGridControls()
    {
        var snapshot = _gridLayoutPanel.Controls.Cast<Control>().ToList();

        _gridLayoutPanel.Controls.Clear();
        _tilePanels.Clear();

        foreach (var control in snapshot)
        {
            control.Dispose();
        }
    }

    private Panel CreatePlaceholderPanel(int row, int column)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new(LogicalToDeviceUnits(2)),
            BackColor = Color.WhiteSmoke,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true,
            Tag = new CellPosition(row, column),
        };

        panel.DragEnter += _dragDrop!.HandleCellDragEnter;
        panel.DragDrop += _dragDrop.HandleCellDragDrop;
        return panel;
    }

    private Panel CreateTilePanel(DashboardTileType type)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new(LogicalToDeviceUnits(2)),
            BackColor = Color.LightSteelBlue,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true,
            Cursor = Cursors.SizeAll,
            Tag = type,
        };

        var label = new Label
        {
            Text = type.Title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.SizeAll,
        };

        panel.Controls.Add(label);

        panel.MouseDown += _dragDrop!.HandleTileMouseDown;
        panel.DragEnter += _dragDrop.HandleCellDragEnter;
        panel.DragDrop += _dragDrop.HandleCellDragDrop;
        label.MouseDown += _dragDrop.HandleTileMouseDown;

        return panel;
    }

    private void UpdatePaletteEnabled()
    {
        foreach (var (type, button) in _paletteCards)
        {
            button.Enabled = !_placedTiles.ContainsKey(type);
        }
    }

    private void PlaceOrMoveTile(DashboardTileType type, int row, int column)
    {
        var columnCount = (int)_gridColumnsNumeric.Value;
        var rowCount = (int)_gridRowsNumeric.Value;

        if (row < 0 || row >= rowCount || column < 0 || column >= columnCount)
        {
            return;
        }

        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            placed = new()
            {
                ColumnSpan = 1,
                RowSpan = 1,
            };

            _placedTiles[type] = placed;
        }

        placed.Row = row;
        placed.Column = column;
        DashboardLayoutCalculator.ClampPlacement(placed, columnCount, rowCount);

        RebuildGrid();
        OnChanged();
    }

    private void OnChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}
