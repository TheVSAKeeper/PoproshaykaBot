using PoproshaykaBot.Core.Chat;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public record RawChatMessageReceived(ChatMessage Message, DateTimeOffset Timestamp) : EventBase;
