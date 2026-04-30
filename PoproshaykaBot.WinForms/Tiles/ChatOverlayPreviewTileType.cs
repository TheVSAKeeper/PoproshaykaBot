using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed class ChatOverlayPreviewTileType() : DashboardTileType(TypeId, "👁️ OBS Чат")
{
    public const string TypeId = "chat-overlay-preview";

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<ChatOverlayPreviewTileControl>();
    }
}
