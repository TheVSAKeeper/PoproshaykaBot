using PoproshaykaBot.Core.Domain.Models.Chat;

namespace PoproshaykaBot.Core.Infrastructure.Interfaces;

public interface IChatDisplay
{
    void AddChatMessage(ChatMessageData chatMessage);

    void ClearChat();
}
