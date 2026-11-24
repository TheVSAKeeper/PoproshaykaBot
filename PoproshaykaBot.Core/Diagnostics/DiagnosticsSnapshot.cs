using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Diagnostics;

public sealed record PlatformChatDiagnostics(
    StreamingPlatform Platform,
    string DisplayName,
    ChatConnectionStatus? Status);

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
    YouTubeQuotaStatus? YouTubeQuota = null,
    string? ActiveBroadcastSaveError = null,
    IReadOnlyList<PlatformChatDiagnostics>? PlatformChats = null);
