using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public readonly record struct TrackedChatSend(ChatEnqueueResult EnqueueResult, Task<ChatAccountSendResult>? Delivery = null);

public interface IPlatformMessenger
{
    ChatEnqueueResult Send(
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null);

    TrackedChatSend SendTracked(string text, ChatSendCategory category)
        => new(Send(text, category: category));

    void Reply(
        string replyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null);

    void Whisper(
        string toUserId,
        string fallbackReplyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null);
}
