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
        var columnCount = Math.Clamp(layout.ColumnCount, DashboardLayoutDefaults.MinColumnCount, DashboardLayoutDefaults.MaxColumnCount);
        var rowCount = Math.Clamp(layout.RowCount, DashboardLayoutDefaults.MinRowCount, DashboardLayoutDefaults.MaxRowCount);

        _tilesTableLayoutPanel.SuspendLayout();

        try
        {
            _tilesTableLayoutPanel.ColumnStyles.Clear();
            _tilesTableLayoutPanel.RowStyles.Clear();
            _tilesTableLayoutPanel.ColumnCount = columnCount;
            _tilesTableLayoutPanel.RowCount = rowCount;

            for (var column = 0; column < columnCount; column++)
            {
                _tilesTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F / columnCount));
            }

            for (var row = 0; row < rowCount; row++)
            {
                _tilesTableLayoutPanel.RowStyles.Add(new(SizeType.Percent, 100F / rowCount));
            }

            foreach (var host in _hosts.Values)
            {
                host.Visible = false;
            }

            var seenTypes = new HashSet<DashboardTileType>();

            foreach (var tile in layout.Tiles.Where(tile => tile.IsVisible))
            {
                var type = DashboardTileCatalog.Find(tile.TypeId);

                if (type == null || !seenTypes.Add(type) || !_hosts.TryGetValue(type, out var host))
                {
                    continue;
                }

                var row = Math.Clamp(tile.Row, 0, rowCount - 1);
                var column = Math.Clamp(tile.Column, 0, columnCount - 1);
                var columnSpan = Math.Clamp(tile.ColumnSpan, 1, columnCount - column);
                var rowSpan = Math.Clamp(tile.RowSpan, 1, rowCount - row);

                _tilesTableLayoutPanel.SetCellPosition(host, new(column, row));
                _tilesTableLayoutPanel.SetColumnSpan(host, columnSpan);
                _tilesTableLayoutPanel.SetRowSpan(host, rowSpan);
                host.Visible = true;
            }
        }
        finally
        {
            _tilesTableLayoutPanel.ResumeLayout();
        }
    }
}
