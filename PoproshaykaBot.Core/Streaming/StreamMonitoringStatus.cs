namespace PoproshaykaBot.Core.Streaming;

public enum StreamMonitoringStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Failed = 4,
}
