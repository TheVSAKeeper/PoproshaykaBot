using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Platforms;

public enum ChannelMonitoringAvailability
{
    Disabled = 0,
    Active = 1,
    Degraded = 2,
    Unavailable = 3,
}

public enum MonitoringUnavailableReason
{
    None = 0,
    NotSupportedByPlatform = 1,
    MonitoringDisabled = 2,
    MissingBroadcasterAuth = 3,
    MissingBotAuth = 4,
    AccessUnverified = 5,
    QuotaExhausted = 6,
    SubscriptionLimitReached = 7,
    MultipleActiveBroadcasts = 8,
    TransportDisconnected = 9,
    InvalidChannel = 10,
}

public sealed record ChannelMonitoringSnapshot(
    ChannelKey Channel,
    string ChannelDisplayName,
    ChannelMonitoringAvailability Availability,
    MonitoringUnavailableReason Reason,
    string? Detail,
    StreamStatus Status,
    StreamInfo? Stream,
    DateTimeOffset? ObservedAtUtc,
    bool IsInitialBaseline);

public sealed record ChannelLiveProbeResult(
    bool IsLive,
    bool IsChatReachable,
    StreamInfo? Stream,
    string? ChatSessionToken = null,
    string? FailureReason = null);

public sealed record ResolvedChannelIdentity(
    ChannelKey Channel,
    string DisplayName,
    string Handle,
    string ChannelUrl);

public sealed record ChannelAccessVerification(
    bool IsAllowed,
    string? SubjectId,
    string? FailureReason = null);

public sealed record PlatformChatSessionTarget(
    ChannelCatalogEntry Channel,
    long SwitchGeneration,
    string? ConfirmedStreamId = null,
    string? ConfirmedChatSessionToken = null);

public interface IPlatformChannelAdapter
{
    StreamingPlatform Platform { get; }

    PlatformCapabilities Capabilities { get; }

    IReadOnlyList<PlatformAccount>? ListAccounts() => null;

    Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(
        string rawInput,
        string? preferredContextId,
        CancellationToken cancellationToken);

    Task<ChannelAccessVerification> VerifyChannelAccessAsync(
        ChannelKey channel,
        PlatformAuthRole role,
        string contextId,
        CancellationToken cancellationToken);

    Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(
        ChannelCatalogEntry channel,
        CancellationToken cancellationToken);
}

public interface IPlatformChannelMonitor : IStreamHostedComponent
{
    StreamingPlatform Platform { get; }

    ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel);

    IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots();

    Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default);
}
