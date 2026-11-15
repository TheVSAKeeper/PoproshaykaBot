using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Platforms.Auth;

public class PlatformAuthStatusReporter(ILogger logger, string platformLabel)
{
    public event Action<PlatformAuthRole, string>? StatusChanged;

    public void Report(PlatformAuthRole role, string status)
    {
        logger.LogInformation("{Platform} OAuth статус ({Role}): {Status}", platformLabel, role, status);
        StatusChanged?.Invoke(role, status);
    }

    public void ClearSubscribers()
    {
        StatusChanged = null;
    }
}
