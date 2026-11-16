using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public record RawChatMessageReceived(ChatMessage Message, DateTimeOffset Timestamp, bool IsReplacement = false) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => Message.Platform;
}
