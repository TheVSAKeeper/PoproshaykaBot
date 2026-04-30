using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class LogsTileType() : DashboardTileType(TypeId, "📜 Логи")
{
    public const string TypeId = "logs";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<LogsTileControl>();
    }
}
