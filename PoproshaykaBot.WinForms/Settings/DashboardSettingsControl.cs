using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;
using System.ComponentModel;

namespace PoproshaykaBot.WinForms.Settings;

public sealed partial class DashboardSettingsControl : UserControl
{
    private readonly Dictionary<DashboardTileType, Button> _paletteCards = [];
    private readonly Dictionary<DashboardTileType, PlacedTile> _placedTiles = [];
    private readonly Dictionary<DashboardTileType, Panel> _tilePanels = [];
    private DashboardLayoutSettings? _pendingLayout;
    private bool _initialized;
    private bool _suppressEvents;

    public DashboardSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(UiSettings ui)
    {
        var layout = ui.Dashboard is { Tiles.Count: > 0 }
            ? ui.Dashboard
            : DashboardLayoutDefaults.Create();

        if (_initialized)
        {
            ApplyLayout(layout);
        }
        else
        {
            _pendingLayout = layout;
        }
    }

    public void SaveSettings(UiSettings ui)
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = (int)_gridColumnsNumeric.Value,
            RowCount = (int)_gridRowsNumeric.Value,
        };

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
            });
        }

        ui.Dashboard = layout;
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

        BuildPalette();
        ApplyLayout(_pendingLayout ?? DashboardLayoutDefaults.Create());
        _pendingLayout = null;
    }

    private void OnPaletteButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (sender is not Button { Enabled: true } button)
        {
            return;
        }

        if (button.Tag is not DashboardTileType type)
        {
            return;
        }

        button.DoDragDrop(new TileDragPayload(type, false), DragDropEffects.Copy);
    }

    private void OnTilePanelMouseDown(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var owner = control.Tag is DashboardTileType ? control : control.Parent;

        if (owner?.Tag is not DashboardTileType type || owner == null)
        {
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            _tileContextMenu.Tag = type;
            _tileContextMenu.Show(control, e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        owner.DoDragDrop(new TileDragPayload(type, true), DragDropEffects.Move);
    }

    private void OnCellDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TileDragPayload)) is TileDragPayload payload)
        {
            e.Effect = payload.FromGrid ? DragDropEffects.Move : DragDropEffects.Copy;
        }
    }

    private void OnCellDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TileDragPayload)) is not TileDragPayload payload)
        {
            return;
        }

        if (sender is not Control control)
        {
            return;
        }

        switch (control.Tag)
        {
            case CellPosition pos:
                PlaceOrMoveTile(payload.Type, pos.Row, pos.Column);
                break;

            case DashboardTileType targetType when _placedTiles.TryGetValue(targetType, out var target):
                if (targetType == payload.Type)
                {
                    return;
                }

                PlaceOrMoveTile(payload.Type, target.Row, target.Column);
                break;
        }
    }

    private void OnTileContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_tileContextMenu.Tag is not DashboardTileType type || !_placedTiles.TryGetValue(type, out var placed))
        {
            e.Cancel = true;
            return;
        }

        var oldItems = _tileContextMenu.Items.Cast<ToolStripItem>().ToList();
        _tileContextMenu.Items.Clear();

        foreach (var oldItem in oldItems)
        {
            oldItem.Dispose();
        }

        var columnCount = (int)_gridColumnsNumeric.Value;
        var rowCount = (int)_gridRowsNumeric.Value;
        var maxColumnSpan = Math.Max(1, columnCount - placed.Column);
        var maxRowSpan = Math.Max(1, rowCount - placed.Row);

        var widthMenu = new ToolStripMenuItem($"Ширина: {placed.ColumnSpan}");

        for (var i = 1; i <= maxColumnSpan; i++)
        {
            var span = i;
            var item = new ToolStripMenuItem(span.ToString())
            {
                Checked = span == placed.ColumnSpan,
            };

            item.Click += (_, _) => SetTileColumnSpan(type, span);
            widthMenu.DropDownItems.Add(item);
        }

        var heightMenu = new ToolStripMenuItem($"Высота: {placed.RowSpan}");

        for (var i = 1; i <= maxRowSpan; i++)
        {
            var span = i;
            var item = new ToolStripMenuItem(span.ToString())
            {
                Checked = span == placed.RowSpan,
            };

            item.Click += (_, _) => SetTileRowSpan(type, span);
            heightMenu.DropDownItems.Add(item);
        }

        var removeItem = new ToolStripMenuItem("Удалить плитку");
        removeItem.Click += (_, _) => RemoveTile(type);

        _tileContextMenu.Items.Add(widthMenu);
        _tileContextMenu.Items.Add(heightMenu);
        _tileContextMenu.Items.Add(new ToolStripSeparator());
        _tileContextMenu.Items.Add(removeItem);
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

    private void BuildPalette()
    {
        _paletteFlowLayoutPanel.SuspendLayout();

        try
        {
            foreach (var type in DashboardTileCatalog.All)
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

                button.MouseDown += OnPaletteButtonMouseDown;
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
            var type = DashboardTileCatalog.Find(tile.TypeId);

            if (type == null || !seen.Add(type))
            {
                continue;
            }

            var row = Math.Clamp(tile.Row, 0, rowCount - 1);
            var column = Math.Clamp(tile.Column, 0, columnCount - 1);

            _placedTiles[type] = new()
            {
                Row = row,
                Column = column,
                ColumnSpan = Math.Clamp(tile.ColumnSpan, 1, columnCount - column),
                RowSpan = Math.Clamp(tile.RowSpan, 1, rowCount - row),
            };
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

            var occupied = new bool[rowCount, columnCount];

            foreach (var (_, placed) in _placedTiles)
            {
                placed.Row = Math.Clamp(placed.Row, 0, rowCount - 1);
                placed.Column = Math.Clamp(placed.Column, 0, columnCount - 1);
                placed.ColumnSpan = Math.Clamp(placed.ColumnSpan, 1, columnCount - placed.Column);
                placed.RowSpan = Math.Clamp(placed.RowSpan, 1, rowCount - placed.Row);

                for (var r = placed.Row; r < placed.Row + placed.RowSpan; r++)
                {
                    for (var c = placed.Column; c < placed.Column + placed.ColumnSpan; c++)
                    {
                        occupied[r, c] = true;
                    }
                }
            }

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

        panel.DragEnter += OnCellDragEnter;
        panel.DragDrop += OnCellDragDrop;
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

        panel.MouseDown += OnTilePanelMouseDown;
        panel.DragEnter += OnCellDragEnter;
        panel.DragDrop += OnCellDragDrop;
        label.MouseDown += OnTilePanelMouseDown;

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

        if (_placedTiles.TryGetValue(type, out var existing))
        {
            existing.Row = row;
            existing.Column = column;
            existing.ColumnSpan = Math.Clamp(existing.ColumnSpan, 1, columnCount - column);
            existing.RowSpan = Math.Clamp(existing.RowSpan, 1, rowCount - row);
        }
        else
        {
            _placedTiles[type] = new()
            {
                Row = row,
                Column = column,
                ColumnSpan = 1,
                RowSpan = 1,
            };
        }

        RebuildGrid();
        OnChanged();
    }

    private void SetTileColumnSpan(DashboardTileType type, int span)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.ColumnSpan = span;
        RebuildGrid();
        OnChanged();
    }

    private void SetTileRowSpan(DashboardTileType type, int span)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.RowSpan = span;
        RebuildGrid();
        OnChanged();
    }

    private void RemoveTile(DashboardTileType type)
    {
        if (!_placedTiles.Remove(type))
        {
            return;
        }

        RebuildGrid();
        OnChanged();
    }

    private void OnChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PlacedTile
    {
        public int Row { get; set; }

        public int Column { get; set; }

        public int ColumnSpan { get; set; }

        public int RowSpan { get; set; }
    }

    private sealed record CellPosition(int Row, int Column);

    private sealed record TileDragPayload(DashboardTileType Type, bool FromGrid);
}
