using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Quota;

public interface IYouTubeQuotaGovernor
{
    bool CanAfford(YouTubeQuotaPriority priority, int cost);

    bool TryReserve(
        PlatformAuthRole role,
        YouTubeQuotaPriority priority,
        int cost,
        out YouTubeQuotaLease lease);

    void MarkExhaustedFromServer(PlatformAuthRole role);

    YouTubeQuotaStatus CaptureDiagnostics();
}
