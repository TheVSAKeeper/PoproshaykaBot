using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthStatusReporter(ILogger<YouTubeOAuthStatusReporter> logger)
{
    public event Action<YouTubeOAuthRole, string>? StatusChanged;

    public void Report(YouTubeOAuthRole role, string status)
    {
        logger.LogInformation("YouTube OAuth статус ({Role}): {Status}", role, status);
        StatusChanged?.Invoke(role, status);
    }

    public void ClearSubscribers()
    {
        StatusChanged = null;
    }
}
