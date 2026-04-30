using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class PollsControlTileType() : DashboardTileType(TypeId, "🗳 Голосования", maxWidth: 500)
{
    public const string TypeId = "polls-control";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<PollsControlPanel>();
    }
}
