using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Infrastructure.Interfaces;
using PoproshaykaBot.Core.Infrastructure.Persistence;

namespace PoproshaykaBot.Core.Application.Chat;

public class ChatHistoryManager(SettingsManager settingsManager)
{
    private readonly object _sync = new();
    private readonly LinkedList<ChatMessageData> _chatHistory = [];
    private readonly List<IChatDisplay> _chatDisplays = [];

    public void AddMessage(ChatMessageData chatMessage)
    {
        lock (_sync)
        {
            _chatHistory.AddLast(chatMessage);

            var max = Math.Max(1, settingsManager.Current.Twitch.Infrastructure.ChatHistoryMaxItems);

            if (_chatHistory.Count > max)
            {
                _chatHistory.RemoveFirst();
            }
        }

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
        if (!_chatDisplays.Contains(chatDisplay))
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
        lock (_sync)
        {
            return _chatHistory.ToList();
        }
    }

    public void ClearHistory()
    {
        lock (_sync)
        {
            _chatHistory.Clear();
        }

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
