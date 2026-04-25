using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.WinForms.Settings;

public sealed partial class DashboardSettingsControl : UserControl
{
    private readonly List<DashboardTileRowControl> _rows = [];

    public DashboardSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public IControlFactory ControlFactory { get; internal init; } = null!;

    public void LoadSettings(UiSettings ui)
    {
        var layout = ui.Dashboard?.Tiles.Count > 0
            ? ui.Dashboard
            : DashboardLayoutDefaults.Create();

        RebuildRows(layout);
    }

    public void SaveSettings(UiSettings ui)
    {
        var layout = new DashboardLayoutSettings();

        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];

            if (row.TileType == null)
            {
                continue;
            }

            layout.Tiles.Add(new()
            {
                Id = row.TileType.Id,
                TypeId = row.TileType.Id,
                Order = index,
                ColumnSpan = row.ColumnSpan,
                RowSpan = row.RowSpan,
                IsVisible = row.IsTileVisible,
            });
        }

        ui.Dashboard = layout;
    }

    private void OnResetLayoutButtonClicked(object? sender, EventArgs e)
    {
        RebuildRows(DashboardLayoutDefaults.Create());
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowMoveUpRequested(object? sender, EventArgs e)
    {
        if (sender is not DashboardTileRowControl row)
        {
            return;
        }

        var index = _rows.IndexOf(row);

        if (index <= 0)
        {
            return;
        }

        SwapRows(index, index - 1);
    }

    private void OnRowMoveDownRequested(object? sender, EventArgs e)
    {
        if (sender is not DashboardTileRowControl row)
        {
            return;
        }

        var index = _rows.IndexOf(row);

        if (index < 0 || index >= _rows.Count - 1)
        {
            return;
        }

        SwapRows(index, index + 1);
    }

    private void OnRowChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRows(DashboardLayoutSettings layout)
    {
        _rowsTableLayoutPanel.SuspendLayout();

        try
        {
            foreach (var row in _rows)
            {
                _rowsTableLayoutPanel.Controls.Remove(row);
                row.Changed -= OnRowChanged;
                row.MoveUpRequested -= OnRowMoveUpRequested;
                row.MoveDownRequested -= OnRowMoveDownRequested;
                row.Dispose();
            }

            _rows.Clear();

            var seenTypes = new HashSet<DashboardTileType>();
            var resolvedTiles = new List<(DashboardTileSettings Tile, DashboardTileType Type)>();

            foreach (var tile in layout.Tiles)
            {
                var type = DashboardTileCatalog.Find(tile.TypeId);

                if (type == null || !seenTypes.Add(type))
                {
                    continue;
                }

                resolvedTiles.Add((tile, type));
            }

            var headerRowHeight = (float)LogicalToDeviceUnits(30);
            var tileRowHeight = (float)LogicalToDeviceUnits(36);

            _rowsTableLayoutPanel.RowStyles.Clear();
            _rowsTableLayoutPanel.RowStyles.Add(new(SizeType.Absolute, headerRowHeight));

            for (var index = 0; index < resolvedTiles.Count; index++)
            {
                _rowsTableLayoutPanel.RowStyles.Add(new(SizeType.Absolute, tileRowHeight));
            }

            _rowsTableLayoutPanel.RowStyles.Add(new(SizeType.Percent, 100F));
            _rowsTableLayoutPanel.RowCount = resolvedTiles.Count + 2;

            for (var index = 0; index < resolvedTiles.Count; index++)
            {
                var (tile, type) = resolvedTiles[index];
                var row = ControlFactory.Create<DashboardTileRowControl>();
                row.Dock = DockStyle.Fill;
                row.Margin = new(0);
                row.Configure(type, tile);
                row.Changed += OnRowChanged;
                row.MoveUpRequested += OnRowMoveUpRequested;
                row.MoveDownRequested += OnRowMoveDownRequested;

                _rowsTableLayoutPanel.Controls.Add(row, 0, index + 1);
                _rows.Add(row);
            }

            UpdateMoveButtonStates();
        }
        finally
        {
            _rowsTableLayoutPanel.ResumeLayout();
        }
    }

    private void UpdateMoveButtonStates()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            _rows[index].MoveUpEnabled = index > 0;
            _rows[index].MoveDownEnabled = index < _rows.Count - 1;
        }
    }

    private void SwapRows(int firstIndex, int secondIndex)
    {
        _rowsTableLayoutPanel.SuspendLayout();

        try
        {
            _rowsTableLayoutPanel.SetRow(_rows[firstIndex], secondIndex + 1);
            _rowsTableLayoutPanel.SetRow(_rows[secondIndex], firstIndex + 1);
            (_rows[firstIndex], _rows[secondIndex]) = (_rows[secondIndex], _rows[firstIndex]);
            UpdateMoveButtonStates();
        }
        finally
        {
            _rowsTableLayoutPanel.ResumeLayout();
        }

        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}
