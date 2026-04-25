using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Tiles;

public abstract class DashboardTileType(string id, string title)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public abstract Control CreateBody(IControlFactory factory);
}

public sealed class StreamInfoTileType : DashboardTileType
{
    public static readonly StreamInfoTileType Instance = new();

    private StreamInfoTileType() : base("stream-info", "🔴 Стрим")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<StreamInfoWidget>();
    }
}

public sealed class BroadcastStatusTileType : DashboardTileType
{
    public static readonly BroadcastStatusTileType Instance = new();

    private BroadcastStatusTileType() : base("broadcast-status", "📢 Рассылка")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<BroadcastInfoWidget>();
    }
}

public sealed class LogsTileType : DashboardTileType
{
    public static readonly LogsTileType Instance = new();

    private LogsTileType() : base("logs", "📜 Логи")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<LogsTileControl>();
    }
}

public sealed class TwitchChatTileType : DashboardTileType
{
    public static readonly TwitchChatTileType Instance = new();

    private TwitchChatTileType() : base("twitch-chat", "💬 Чат Twitch")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<ChatDisplay>();
    }
}

public sealed class ChatOverlayPreviewTileType : DashboardTileType
{
    public static readonly ChatOverlayPreviewTileType Instance = new();

    private ChatOverlayPreviewTileType() : base("chat-overlay-preview", "👁️ OBS Чат")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<ChatOverlayPreviewControl>();
    }
}

public sealed class BroadcastProfilesTileType : DashboardTileType
{
    public static readonly BroadcastProfilesTileType Instance = new();

    private BroadcastProfilesTileType() : base("broadcast-profiles", "🎛 Профили")
    {
    }

    public override Control CreateBody(IControlFactory factory)
    {
        return factory.Create<BroadcastProfilesPanel>();
    }
}
