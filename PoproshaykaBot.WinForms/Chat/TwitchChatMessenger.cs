using PoproshaykaBot.WinForms.Twitch.Chat;

namespace PoproshaykaBot.WinForms.Chat;

public interface IChatMessenger
{
    void Send(string text);
    void Reply(string replyToMessageId, string text);
}

public sealed class TwitchChatMessenger(ChatSender chatSender) : IChatMessenger
{
    public void Send(string text)
    {
        _ = chatSender.EnqueueAsync(text, null, CancellationToken.None);
    }

    public void Reply(string replyToMessageId, string text)
    {
        _ = chatSender.EnqueueAsync(text, replyToMessageId, CancellationToken.None);
    }
}
