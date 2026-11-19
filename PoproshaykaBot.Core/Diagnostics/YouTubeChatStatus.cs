namespace PoproshaykaBot.Core.Diagnostics;

public enum YouTubeChatTransport
{
    None = 0,
    GrpcStream = 1,
    RestPolling = 2,
    InnerTube = 3,
}

public sealed record YouTubeChatStatus(
    ConnectionState State,
    YouTubeChatTransport Transport,
    ChatReadIssue Issue,
    string? Channel,
    string? LiveChatId,
    DateTimeOffset? ConnectedSince,
    DateTimeOffset? LastBatchAt,
    DateTimeOffset? LastMessageAt,
    long ReceivedMessages,
    int GapCount,
    int ReconnectAttempt)
{
    public ChatConnectionStatus ToChatConnectionStatus() =>
        new(State, Channel, ConnectedSince, LastMessageAt, Issue);
}

public interface IYouTubeChatDiagnostics
{
    YouTubeChatStatus CaptureDiagnostics();
}
