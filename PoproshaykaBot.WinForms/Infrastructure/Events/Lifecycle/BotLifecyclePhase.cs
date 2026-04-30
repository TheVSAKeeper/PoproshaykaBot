namespace PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;

public enum BotLifecyclePhase
{
    Idle = 0,
    Connecting = 1,
    Connected = 2,
    Disconnecting = 3,
    Disconnected = 4,
    Cancelled = 5,
    Failed = 6,
}
