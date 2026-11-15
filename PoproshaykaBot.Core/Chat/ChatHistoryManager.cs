using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatHistoryManager(SettingsManager settingsManager, IEventBus eventBus)
{
    private readonly Lock _sync = new();
    private readonly LinkedList<(ChatMessageData Message, string Channel)> _chatHistory = [];

    public void AddMessage(ChatMessageData chatMessage, string channel = "")
    {
        lock (_sync)
        {
            _chatHistory.AddLast((chatMessage, channel));

            var max = Math.Max(1, settingsManager.Current.ChatHistoryMaxItems);

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
            return _chatHistory.Select(static entry => entry.Message).ToArray();
        }
    }

    public void ClearHistory()
    {
        (StreamingPlatform Platform, string Channel)[] chats;
        lock (_sync)
        {
            chats = _chatHistory.Select(static entry => (entry.Message.Platform, entry.Channel)).Distinct().ToArray();
            _chatHistory.Clear();
        }

        foreach (var (platform, channel) in chats)
        {
            _ = eventBus.PublishAsync(new ChatHistoryCleared(platform, channel));
        }
    }
}
