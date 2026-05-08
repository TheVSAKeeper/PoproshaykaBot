using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamMonitoringStatusChanged(StreamMonitoringStatus Status, string? Detail = null) : EventBase;
