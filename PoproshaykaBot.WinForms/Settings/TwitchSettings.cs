using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Settings;

public sealed class TwitchSettings
{
    public string Channel { get; set; } = "bobito217";

    public int MessagesAllowedInPeriod { get; set; } = 750;

    public int ThrottlingPeriodSeconds { get; set; } = 30;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = "http://localhost:8080";

    public int HttpServerPort { get; set; } = 8080;

    public MessageSettings Messages { get; set; } = new();

    public AutoBroadcastSettings AutoBroadcast { get; set; } = new();

    public BotLifecycleAutomationSettings BotLifecycleAutomation { get; set; } = new();

    public InfrastructureSettings Infrastructure { get; set; } = new();
}
