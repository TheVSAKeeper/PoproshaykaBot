using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;
using System.ComponentModel;

namespace PoproshaykaBot.WinForms.Settings;

public sealed partial class DashboardSettingsControl : UserControl
{
    private static readonly int[] MaxHeightPresets = [100, 150, 200, 250, 300, 400, 500];
    private static readonly int[] MaxWidthPresets = [200, 300, 400, 500, 600, 800];
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

        var collapsedByType = ui.Dashboard?.Tiles
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

        var maxWidthMenu = BuildMaxSizeMenu("Макс. ширина",
            MaxWidthPresets,
            placed.MaxWidth,
            type.MaxWidth,
            value => SetTileMaxWidth(type, value));

        var maxHeightMenu = BuildMaxSizeMenu("Макс. высота",
            MaxHeightPresets,
            placed.MaxHeight,
            type.MaxHeight,
            value => SetTileMaxHeight(type, value));

        var removeItem = new ToolStripMenuItem("Удалить плитку");
        removeItem.Click += (_, _) => RemoveTile(type);

        _tileContextMenu.Items.Add(widthMenu);
        _tileContextMenu.Items.Add(heightMenu);
        _tileContextMenu.Items.Add(new ToolStripSeparator());
        _tileContextMenu.Items.Add(maxWidthMenu);
        _tileContextMenu.Items.Add(maxHeightMenu);
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

    private static int? PromptForCustomSize(IWin32Window owner, string label, int initial)
    {
        const int minValue = 50;
        const int maxValue = 5000;
        var clamped = Math.Clamp(initial, minValue, maxValue);

        using var form = new Form
        {
            Text = label,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new(12),
        };

        var promptLabel = new Label
        {
            Text = "Значение, px:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new(0, 6, 8, 0),
        };

        var numeric = new NumericUpDown
        {
            Minimum = minValue,
            Maximum = maxValue,
            Increment = 10,
            Value = clamped,
            Width = 100,
            Margin = new(0, 4, 0, 0),
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new(0, 8, 0, 0),
        };

        var cancelButton = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
        };

        var okButton = new Button
        {
            Text = "ОК",
            DialogResult = DialogResult.OK,
            AutoSize = true,
        };

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(numeric, 1, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.SetColumnSpan(buttons, 2);

        form.Controls.Add(layout);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog(owner) == DialogResult.OK
            ? (int)numeric.Value
            : null;
    }

    private ToolStripMenuItem BuildMaxSizeMenu(
        string label,
        IReadOnlyList<int> presets,
        int? overrideValue,
        int? typeDefault,
        Action<int?> setValue)
    {
        var isExplicitAuto = overrideValue is <= 0;
        var effective = isExplicitAuto ? null : overrideValue ?? typeDefault;

        string headerText;

        if (isExplicitAuto)
        {
            headerText = $"{label}: авто";
        }
        else if (effective.HasValue)
        {
            headerText = overrideValue.HasValue
                ? $"{label}: {effective.Value}px"
                : $"{label}: {effective.Value}px (по умолч.)";
        }
        else
        {
            headerText = $"{label}: авто (по умолч.)";
        }

        var menu = new ToolStripMenuItem(headerText);

        var defaultLabel = typeDefault.HasValue
            ? $"По умолчанию ({typeDefault.Value}px)"
            : "По умолчанию (авто)";

        var defaultItem = new ToolStripMenuItem(defaultLabel)
        {
            Checked = !overrideValue.HasValue,
        };

        defaultItem.Click += (_, _) => setValue(null);
        menu.DropDownItems.Add(defaultItem);

        if (typeDefault.HasValue)
        {
            var autoItem = new ToolStripMenuItem("Авто")
            {
                Checked = isExplicitAuto,
            };

            autoItem.Click += (_, _) => setValue(0);
            menu.DropDownItems.Add(autoItem);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());

        var isCustomValue = overrideValue is > 0 && !presets.Contains(overrideValue.Value);

        foreach (var preset in presets)
        {
            var value = preset;
            var item = new ToolStripMenuItem($"{value}px")
            {
                Checked = overrideValue == value,
            };

            item.Click += (_, _) => setValue(value);
            menu.DropDownItems.Add(item);
        }

        var customLabel = isCustomValue
            ? $"Указать... ({overrideValue!.Value}px)"
            : "Указать...";

        var customItem = new ToolStripMenuItem(customLabel)
        {
            Checked = isCustomValue,
        };

        customItem.Click += (_, _) =>
        {
            var initial = overrideValue is > 0 ? overrideValue.Value : typeDefault ?? presets[0];
            var custom = PromptForCustomSize(FindForm() ?? (IWin32Window)this, label, initial);

            if (custom.HasValue)
            {
                setValue(custom.Value);
            }
        };

        menu.DropDownItems.Add(customItem);

        return menu;
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
                MaxHeight = tile.MaxHeight,
                MaxWidth = tile.MaxWidth,
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

    private void SetTileMaxHeight(DashboardTileType type, int? value)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.MaxHeight = value;
        OnChanged();
    }

    private void SetTileMaxWidth(DashboardTileType type, int? value)
    {
        if (!_placedTiles.TryGetValue(type, out var placed))
        {
            return;
        }

        placed.MaxWidth = value;
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

        public int? MaxHeight { get; set; }

        public int? MaxWidth { get; set; }
    }

    private sealed record CellPosition(int Row, int Column);

    private sealed record TileDragPayload(DashboardTileType Type, bool FromGrid);
}
