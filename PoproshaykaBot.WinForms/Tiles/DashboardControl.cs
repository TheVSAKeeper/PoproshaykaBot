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

    private static int? ResolveMaxSize(int? overrideValue, int? typeDefault)
    {
        return overrideValue switch
        {
            null => typeDefault,
            <= 0 => null,
            _ => overrideValue,
        };
    }

    private static int?[] ComputeRowFixedHeights(IReadOnlyList<TilePlacement> placements, int rowCount)
    {
        var heights = new int?[rowCount];

        for (var row = 0; row < rowCount; row++)
        {
            var rowIndex = row;
            var singleRowTiles = placements
                .Where(p => p.RowSpan == 1 && p.Row == rowIndex)
                .ToList();

            var hasFlexSingleRow = singleRowTiles.Any(p => p.MaxHeight == null);

            if (hasFlexSingleRow)
            {
                heights[row] = null;
                continue;
            }

            var compactSingleRow = singleRowTiles
                .Where(p => p.MaxHeight.HasValue)
                .ToList();

            heights[row] = compactSingleRow.Count > 0
                ? compactSingleRow.Max(p => p.MaxHeight!.Value)
                : null;
        }

        return heights;
    }

    private static int?[] ComputeColumnFixedWidths(IReadOnlyList<TilePlacement> placements, int columnCount)
    {
        var widths = new int?[columnCount];

        for (var column = 0; column < columnCount; column++)
        {
            var columnIndex = column;
            var singleColumnTiles = placements
                .Where(p => p.ColumnSpan == 1 && p.Column == columnIndex)
                .ToList();

            var hasFlexSingleColumn = singleColumnTiles.Any(p => p.MaxWidth == null);

            if (hasFlexSingleColumn)
            {
                widths[column] = null;
                continue;
            }

            var compactSingleColumn = singleColumnTiles
                .Where(p => p.MaxWidth.HasValue)
                .ToList();

            widths[column] = compactSingleColumn.Count > 0
                ? compactSingleColumn.Max(p => p.MaxWidth!.Value)
                : null;
        }

        return widths;
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

        var placements = new List<TilePlacement>();
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

            placements.Add(new(host, type, row, column, columnSpan, rowSpan,
                ResolveMaxSize(tile.MaxHeight, type.MaxHeight),
                ResolveMaxSize(tile.MaxWidth, type.MaxWidth)));
        }

        var rowFixedHeights = ComputeRowFixedHeights(placements, rowCount);
        var flexRowCount = rowFixedHeights.Count(height => height == null);
        var percentPerFlexRow = flexRowCount > 0 ? 100F / flexRowCount : 100F / rowCount;

        var columnFixedWidths = ComputeColumnFixedWidths(placements, columnCount);
        var flexColumnCount = columnFixedWidths.Count(width => width == null);
        var percentPerFlexColumn = flexColumnCount > 0 ? 100F / flexColumnCount : 100F / columnCount;

        _tilesTableLayoutPanel.SuspendLayout();

        try
        {
            _tilesTableLayoutPanel.ColumnStyles.Clear();
            _tilesTableLayoutPanel.RowStyles.Clear();
            _tilesTableLayoutPanel.ColumnCount = columnCount;
            _tilesTableLayoutPanel.RowCount = rowCount;

            for (var column = 0; column < columnCount; column++)
            {
                _tilesTableLayoutPanel.ColumnStyles.Add(columnFixedWidths[column] is { } fixedWidth
                    ? new(SizeType.Absolute, LogicalToDeviceUnits(fixedWidth))
                    : new ColumnStyle(SizeType.Percent, percentPerFlexColumn));
            }

            for (var row = 0; row < rowCount; row++)
            {
                _tilesTableLayoutPanel.RowStyles.Add(rowFixedHeights[row] is { } fixedHeight
                    ? new(SizeType.Absolute, LogicalToDeviceUnits(fixedHeight))
                    : new RowStyle(SizeType.Percent, percentPerFlexRow));
            }

            foreach (var host in _hosts.Values)
            {
                host.Visible = false;
            }

            foreach (var placement in placements)
            {
                _tilesTableLayoutPanel.SetCellPosition(placement.Host, new(placement.Column, placement.Row));
                _tilesTableLayoutPanel.SetColumnSpan(placement.Host, placement.ColumnSpan);
                _tilesTableLayoutPanel.SetRowSpan(placement.Host, placement.RowSpan);
                placement.Host.Visible = true;
            }
        }
        finally
        {
            _tilesTableLayoutPanel.ResumeLayout();
        }
    }

    private sealed record TilePlacement(
        DashboardTileHost Host,
        DashboardTileType Type,
        int Row,
        int Column,
        int ColumnSpan,
        int RowSpan,
        int? MaxHeight,
        int? MaxWidth);
}
