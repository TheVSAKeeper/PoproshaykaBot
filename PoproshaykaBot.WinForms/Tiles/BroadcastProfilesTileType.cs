using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class BroadcastProfilesTileType() : DashboardTileType(TypeId, "🎛 Профили", maxWidth: 500)
{
    public const string TypeId = "broadcast-profiles";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<BroadcastProfilesPanel>();
    }
}
