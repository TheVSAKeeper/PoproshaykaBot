using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public interface IPlatformMessenger
{
    ChatEnqueueResult Send(string text, CommandResponseMark? commandResponse = null);

    void Reply(string replyToMessageId, string callerDisplayName, string text, CommandResponseMark? commandResponse = null);

    void Whisper(string toUserId, string fallbackReplyToMessageId, string callerDisplayName, string text, CommandResponseMark? commandResponse = null);
}
