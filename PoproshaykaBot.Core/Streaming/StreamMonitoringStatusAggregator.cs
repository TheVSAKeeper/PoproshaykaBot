using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamMonitoringStatusAggregator
{
    public static (StreamMonitoringStatus Status, TwitchOAuthRole Role)? SelectWorst(
        StreamMonitoringStatus? bot,
        StreamMonitoringStatus? broadcaster)
    {
        if (bot is null && broadcaster is null)
        {
            return null;
        }

        if (bot is null)
        {
            return (broadcaster!.Value, TwitchOAuthRole.Broadcaster);
        }

        if (broadcaster is null)
        {
            return (bot.Value, TwitchOAuthRole.Bot);
        }

        return Severity(bot.Value) >= Severity(broadcaster.Value)
            ? (bot.Value, TwitchOAuthRole.Bot)
            : (broadcaster.Value, TwitchOAuthRole.Broadcaster);
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
