using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Diagnostics;

public enum YouTubeMonitorIssue
{
    None = 0,
    MissingBroadcasterAuth = 1,
    InvalidTargetChannel = 2,
    MultipleActiveBroadcasts = 3,
    QuotaExhausted = 4,
    TransientError = 5,
    MissingBotAuth = 6,
    AccessUnverified = 7,
}

public enum YouTubeMonitoringTransport
{
    None = 0,
    InnerTubeDiscovery = 1,
    DataApi = 2,
    HybridInnerTubeAndDataApi = 3,
}

public sealed record YouTubeMonitoringStatus(
    bool Enabled,
    StreamStatus StreamStatus,
    YouTubeMonitorIssue Issue,
    string? TargetChannelId,
    string? ActiveVideoId,
    string? ActiveLiveChatId,
    int ActiveCandidateCount,
    DateTimeOffset? LastPollAt,
    int ConsecutiveFailures,
    YouTubeMonitoringTransport Transport = YouTubeMonitoringTransport.None);

public interface IYouTubeMonitorDiagnostics
{
    YouTubeMonitoringStatus CaptureDiagnostics();
}
