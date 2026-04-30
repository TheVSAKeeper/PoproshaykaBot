using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tiles;

public abstract class DashboardTileType(string id, string title, int? maxHeight = null, int? maxWidth = null)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public int? MaxHeight { get; } = maxHeight;

    public int? MaxWidth { get; } = maxWidth;

    public abstract Control CreateBody(IControlFactory factory);
}
