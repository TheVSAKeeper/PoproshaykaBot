namespace PoproshaykaBot.Core.Diagnostics;

public enum YouTubeChatTransport
{
    None = 0,
    GrpcStream = 1,
    RestPolling = 2,
}

public enum YouTubeChatIssue
{
    None = 0,
    MissingBotAuth = 1,
    WaitingForActiveBroadcast = 2,
    QuotaExhausted = 3,
    TransientError = 4,
}

public sealed record YouTubeChatStatus(
    ConnectionState State,
    YouTubeChatTransport Transport,
    YouTubeChatIssue Issue,
    string? Channel,
    string? LiveChatId,
    DateTimeOffset? ConnectedSince,
    DateTimeOffset? LastBatchAt,
    DateTimeOffset? LastMessageAt,
    long ReceivedMessages,
    int GapCount,
    int ReconnectAttempt);

public interface IYouTubeChatDiagnostics
{
    YouTubeChatStatus CaptureDiagnostics();
}
