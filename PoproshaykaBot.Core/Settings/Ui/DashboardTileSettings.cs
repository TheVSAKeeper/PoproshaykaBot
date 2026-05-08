namespace PoproshaykaBot.Core.Settings.Ui;

public class DashboardTileSettings
{
    public string Id { get; set; } = string.Empty;
    public string TypeId { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
    public bool IsCollapsed { get; set; }
    public int? MaxHeight { get; set; }
    public int? MaxWidth { get; set; }
}
