using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthStatusReporter(ILogger<OAuthStatusReporter> logger)
{
    public event Action<TwitchOAuthRole, string>? StatusChanged;

    public void Report(TwitchOAuthRole role, string status)
    {
        logger.LogInformation("OAuth статус ({Role}): {Status}", role, status);
        StatusChanged?.Invoke(role, status);
    }

    public void ClearSubscribers()
    {
        StatusChanged = null;
    }
}
