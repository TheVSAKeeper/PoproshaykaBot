using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamMonitoringStatusAggregator
{
    public static (StreamMonitoringStatus Status, PlatformAuthRole Role)? SelectWorst(
        StreamMonitoringStatus? bot,
        StreamMonitoringStatus? broadcaster)
    {
        if (bot is null && broadcaster is null)
        {
            return null;
        }

        if (bot is null)
        {
            return (broadcaster!.Value, PlatformAuthRole.Broadcaster);
        }

        if (broadcaster is null)
        {
            return (bot.Value, PlatformAuthRole.Bot);
        }

        return Severity(bot.Value) >= Severity(broadcaster.Value)
            ? (bot.Value, PlatformAuthRole.Bot)
            : (broadcaster.Value, PlatformAuthRole.Broadcaster);
    }

    private static int Severity(StreamMonitoringStatus status)
    {
        return status switch
        {
            StreamMonitoringStatus.Failed => 4,
            StreamMonitoringStatus.Reconnecting => 3,
            StreamMonitoringStatus.Connecting => 2,
            StreamMonitoringStatus.Disconnected => 1,
            StreamMonitoringStatus.Connected => 0,
            _ => 0,
        };
    }
}
