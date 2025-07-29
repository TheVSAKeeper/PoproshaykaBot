using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public class ChatHistoryManager
{
    private readonly List<ChatMessageData> _chatHistory = [];
    private readonly List<IChatDisplay> _chatDisplays = [];

    public void AddMessage(ChatMessageData chatMessage)
    {
        _chatHistory.Add(chatMessage);

        foreach (var display in _chatDisplays.ToList())
        {
            try
            {
                display.AddChatMessage(chatMessage);
            }
            catch
            {
                UnregisterChatDisplay(display);
            }
        }
    }

    public void RegisterChatDisplay(IChatDisplay chatDisplay)
    {
        if (_chatDisplays.Contains(chatDisplay) == false)
        {
            _chatDisplays.Add(chatDisplay);
        }
    }

    public void UnregisterChatDisplay(IChatDisplay chatDisplay)
    {
        _chatDisplays.Remove(chatDisplay);
    }

    public IEnumerable<ChatMessageData> GetHistory()
    {
        return _chatHistory;
    }

    public void ClearHistory()
    {
        _chatHistory.Clear();

        foreach (var display in _chatDisplays.ToList())
        {
            try
            {
                display.ClearChat();
            }
            catch
            {
                UnregisterChatDisplay(display);
            }
        }
    }
}
