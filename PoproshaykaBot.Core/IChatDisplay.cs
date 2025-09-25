namespace PoproshaykaBot.Core;

public interface IChatDisplay
{
    void AddChatMessage(ChatMessageData chatMessage);

    void ClearChat();
}
