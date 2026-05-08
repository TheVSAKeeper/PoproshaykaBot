using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatHistoryManager(SettingsManager settingsManager, IEventBus eventBus)
{
    private readonly object _sync = new();
    private readonly LinkedList<ChatMessageData> _chatHistory = [];

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
    }

    public IReadOnlyList<ChatMessageData> GetHistory()
    {
        lock (_sync)
        {
            return _chatHistory.ToArray();
        }
    }

    public void ClearHistory()
    {
        lock (_sync)
        {
            _chatHistory.Clear();
        }

        _ = eventBus.PublishAsync(new ChatHistoryCleared());
    }
}
