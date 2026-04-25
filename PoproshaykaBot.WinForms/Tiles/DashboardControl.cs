using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class DashboardControl : UserControl
{
    private readonly Dictionary<DashboardTileType, DashboardTileHost> _hosts = [];
    private bool _initialized;

    public DashboardControl()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager Settings { get; internal init; } = null!;

    [Inject]
    public IControlFactory ControlFactory { get; internal init; } = null!;

    public void ReloadDashboard()
    {
        if (!_initialized)
        {
            return;
        }

        var settings = Settings.Current;
        var layout = settings.Ui.Dashboard;

        if (layout == null || layout.Tiles.Count == 0)
        {
            layout = DashboardLayoutDefaults.Create();
            settings.Ui.Dashboard = layout;
            Settings.SaveSettings(settings);
        }

        ApplyLayout(layout);
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
        InitializeHosts();
        ReloadDashboard();
    }

    private static List<TilePlacement> PlaceTiles(IReadOnlyList<DashboardTileSettings> tiles, out int rowCount)
    {
        var placements = new List<TilePlacement>();
        var row = 0;
        var column = 0;
        rowCount = 0;

        foreach (var tile in tiles)
        {
            var columnSpan = Math.Clamp(tile.ColumnSpan, 1, DashboardLayoutDefaults.ColumnCount);
            var rowSpan = Math.Clamp(tile.RowSpan, 1, DashboardLayoutDefaults.MaxRowSpan);

            if (column + columnSpan > DashboardLayoutDefaults.ColumnCount)
            {
                row++;
                column = 0;
            }

            placements.Add(new(tile, row, column));
            rowCount = Math.Max(rowCount, row + rowSpan);

            column += columnSpan;

            if (column >= DashboardLayoutDefaults.ColumnCount)
            {
                row++;
                column = 0;
            }
        }

        return placements;
    }

    private void InitializeHosts()
    {
        var hostMargin = new Padding(LogicalToDeviceUnits(4));

        _tilesTableLayoutPanel.SuspendLayout();

        try
        {
            foreach (var type in DashboardTileCatalog.All)
            {
                var host = new DashboardTileHost
                {
                    Name = $"_{type.Id}Tile",
                    Dock = DockStyle.Fill,
                    Margin = hostMargin,
                };

                host.SetTitle(type.Title);
                host.SetBody(type.CreateBody(ControlFactory));

                _hosts[type] = host;
                _tilesTableLayoutPanel.Controls.Add(host, 0, 0);

                _ = host.Handle;
                host.Visible = false;
            }
        }
        finally
        {
            _tilesTableLayoutPanel.ResumeLayout();
        }
    }

    private void ApplyLayout(DashboardLayoutSettings layout)
    {
        var visibleTiles = layout.Tiles
            .Where(tile => tile.IsVisible)
            .OrderBy(tile => tile.Order)
            .ToList();

        var placements = PlaceTiles(visibleTiles, out var rowCount);

        _tilesTableLayoutPanel.SuspendLayout();

        try
        {
            _tilesTableLayoutPanel.ColumnStyles.Clear();
            _tilesTableLayoutPanel.RowStyles.Clear();
            _tilesTableLayoutPanel.ColumnCount = DashboardLayoutDefaults.ColumnCount;
            _tilesTableLayoutPanel.RowCount = Math.Max(1, rowCount);

            for (var column = 0; column < DashboardLayoutDefaults.ColumnCount; column++)
            {
                _tilesTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F / DashboardLayoutDefaults.ColumnCount));
            }

            for (var row = 0; row < _tilesTableLayoutPanel.RowCount; row++)
            {
                _tilesTableLayoutPanel.RowStyles.Add(new(SizeType.Percent, 100F / _tilesTableLayoutPanel.RowCount));
            }

            foreach (var host in _hosts.Values)
            {
                host.Visible = false;
            }

            foreach (var placement in placements)
            {
                var type = DashboardTileCatalog.Find(placement.Tile.TypeId);

                if (type == null || !_hosts.TryGetValue(type, out var host))
                {
                    continue;
                }

                _tilesTableLayoutPanel.SetCellPosition(host, new(placement.Column, placement.Row));
                _tilesTableLayoutPanel.SetColumnSpan(host, Math.Clamp(placement.Tile.ColumnSpan, 1, DashboardLayoutDefaults.ColumnCount));
                _tilesTableLayoutPanel.SetRowSpan(host, Math.Clamp(placement.Tile.RowSpan, 1, DashboardLayoutDefaults.MaxRowSpan));
                host.Visible = true;
            }
        }
        finally
        {
            _tilesTableLayoutPanel.ResumeLayout();
        }
    }

    private sealed record TilePlacement(DashboardTileSettings Tile, int Row, int Column);
}
