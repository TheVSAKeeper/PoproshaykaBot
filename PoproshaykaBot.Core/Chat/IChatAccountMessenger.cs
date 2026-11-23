using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public sealed record ChatSenderAccount(string AccountId, PlatformAuthRole Role, string ContextId);

public enum ChatDeliveryStatus
{
    None = 0,
    Sent = 1,
    AuthorizationRequired = 2,
    Rejected = 3,
    Unconfirmed = 4,
    ChatUnavailable = 5,
    QuotaExhausted = 6,
    ObserverMode = 7,
    QueueClosed = 8,
}

public readonly record struct ChatAccountSendResult(
    ChatEnqueueResult EnqueueResult, ChatDeliveryStatus DeliveryStatus = ChatDeliveryStatus.None, int SentParts = 0);

public interface IChatAccountMessenger
{
    bool CanSendAs(ChatSenderAccount account);

    Task<ChatAccountSendResult> SendAsAsync(ChatSenderAccount account, string streamId, string text);
}
