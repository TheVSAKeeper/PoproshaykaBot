namespace PoproshaykaBot.Core.Diagnostics;

public sealed record DiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    MemoryUsage? Memory,
    IReadOnlyList<EventSubStatus> EventSub,
    ChatConnectionStatus? Chat,
    ObsConnectionStatus? Obs,
    SseStatus? Sse,
    ChatQueueStatus? ChatQueue,
    IReadOnlyList<ScheduledJobStatus> Jobs,
    EventBusStatistics? Bus,
    YouTubeMonitoringStatus? YouTubeMonitoring = null,
    YouTubeChatStatus? YouTubeChat = null,
    YouTubeQuotaStatus? YouTubeQuota = null);
