namespace PoproshaykaBot.Core.Diagnostics;

using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record ChatConnectionStatus(
    ConnectionState State,
    string? Channel,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? LastMessageAt,
    ChatReadIssue Issue = ChatReadIssue.None)
{
    internal static ConnectionState? InactiveState(BotLifecyclePhase phase) => phase switch
    {
        BotLifecyclePhase.Connected => null,
        BotLifecyclePhase.Connecting => ConnectionState.Connecting,
        BotLifecyclePhase.Disconnecting or BotLifecyclePhase.Disconnected or BotLifecyclePhase.Cancelled => ConnectionState.Disconnected,
        BotLifecyclePhase.Failed => ConnectionState.Failed,
        _ => ConnectionState.Unknown,
    };
}

public enum ChatReadIssue
{
    None = 0,
    MissingBotAuth = 1,
    WaitingForActiveBroadcast = 2,
    QuotaExhausted = 3,
    TransientError = 4,
    SubscriptionFailed = 5,
    ChatDisabled = 6,
}
