using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamMonitoringStatusChanged(
    StreamingPlatform Platform,
    PlatformAuthRole Role,
    StreamMonitoringStatus Status,
    string? Detail = null) : EventBase, IPlatformEvent;
