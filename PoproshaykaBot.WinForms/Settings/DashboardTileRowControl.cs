using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.WinForms.Settings;

public sealed partial class DashboardTileRowControl : UserControl
{
    public DashboardTileRowControl()
    {
        InitializeComponent();
    }

    public event EventHandler? Changed;

    public event EventHandler? MoveDownRequested;

    public event EventHandler? MoveUpRequested;

    public DashboardTileType? TileType { get; private set; }

    public bool IsTileVisible => _visibleCheckBox.Checked;

    public int ColumnSpan => (int)_columnSpanNumeric.Value;

    public int RowSpan => (int)_rowSpanNumeric.Value;

    public bool MoveUpEnabled
    {
        get => _upButton.Enabled;
        set => _upButton.Enabled = value;
    }

    public bool MoveDownEnabled
    {
        get => _downButton.Enabled;
        set => _downButton.Enabled = value;
    }

    public void Configure(DashboardTileType type, DashboardTileSettings tile)
    {
        TileType = type;
        _titleLabel.Text = type.Title;
        _visibleCheckBox.Checked = tile.IsVisible;

        _columnSpanNumeric.Maximum = DashboardLayoutDefaults.ColumnCount;
        _columnSpanNumeric.Value = Math.Clamp(tile.ColumnSpan, 1, DashboardLayoutDefaults.ColumnCount);

        _rowSpanNumeric.Maximum = DashboardLayoutDefaults.MaxRowSpan;
        _rowSpanNumeric.Value = Math.Clamp(tile.RowSpan, 1, DashboardLayoutDefaults.MaxRowSpan);
    }

    private void OnInputChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUpButtonClicked(object? sender, EventArgs e)
    {
        MoveUpRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownButtonClicked(object? sender, EventArgs e)
    {
        MoveDownRequested?.Invoke(this, EventArgs.Empty);
    }
}
