namespace PoproshaykaBot.Core.YouTube.Quota;

public enum YouTubeQuotaPriority
{
    None = 0,
    CriticalReadAndMonitor = 1,
    CommandReply = 2,
    StandaloneGreeting = 3,
    ScheduledBroadcast = 4,
}
