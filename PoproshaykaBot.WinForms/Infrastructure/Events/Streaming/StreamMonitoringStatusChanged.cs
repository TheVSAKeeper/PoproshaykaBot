using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;

public sealed record StreamMonitoringStatusChanged(StreamMonitoringStatus Status, string? Detail = null) : EventBase;
