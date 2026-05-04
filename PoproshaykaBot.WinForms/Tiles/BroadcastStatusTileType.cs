using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Widgets;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class BroadcastStatusTileType() : DashboardTileType(TypeId, "📢 Рассылка", 170, 360)
{
    public const string TypeId = "broadcast-status";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<BroadcastInfoWidget>();
    }
}
