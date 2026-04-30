using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class TwitchChatTileType() : DashboardTileType(TypeId, "💬 Чат Twitch")
{
    public const string TypeId = "twitch-chat";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<ChatDisplay>();
    }
}
