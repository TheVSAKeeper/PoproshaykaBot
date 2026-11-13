using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Widgets;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class StreamInfoTileType() : DashboardTileType(TypeId, "🔴 Стрим", 240, 420)
{
    public const string TypeId = "stream-info";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<StreamInfoWidget>();
    }
}
