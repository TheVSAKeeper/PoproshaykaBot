using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public interface IChatMessenger
{
    ChatEnqueueResult Send(string text);
    void Reply(string replyToMessageId, string text);
}

public sealed class TwitchChatMessenger(ChatSender chatSender) : IChatMessenger
{
    public ChatEnqueueResult Send(string text)
    {
        return Send(text, null);
    }

    public void Reply(string replyToMessageId, string text)
    {
        Reply(replyToMessageId, text, null);
    }

    public ChatEnqueueResult Send(string text, CommandResponseMark? commandResponse)
    {
        return chatSender.Enqueue(text, null, commandResponse);
    }

    public void Reply(string replyToMessageId, string text, CommandResponseMark? commandResponse)
    {
        _ = chatSender.EnqueueAsync(text, replyToMessageId, commandResponse, CancellationToken.None);
    }

    public void Whisper(string toUserId, string fallbackReplyToMessageId, string text, CommandResponseMark? commandResponse)
    {
        _ = chatSender.EnqueueWhisperAsync(text, toUserId, fallbackReplyToMessageId, commandResponse, CancellationToken.None);
    }
}
