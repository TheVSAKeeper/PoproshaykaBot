namespace PoproshaykaBot.Core.Settings;

public class InfrastructureSettings
{
    public int ChatHistoryMaxItems { get; set; } = 1000;
    public int SseKeepAliveSeconds { get; set; } = 30;
    public int SseGlobalChannelCapacity { get; set; } = 512;
    public int SseClientChannelCapacity { get; set; } = 256;
    public int SseDropLogThrottle { get; set; } = 50;
    public int SseDropNotifyThreshold { get; set; } = 10;
    public int StreamStuckOnlineThresholdSeconds { get; set; } = 120;
    public int OAuthAuthTimeoutMinutes { get; set; } = 5;
}
