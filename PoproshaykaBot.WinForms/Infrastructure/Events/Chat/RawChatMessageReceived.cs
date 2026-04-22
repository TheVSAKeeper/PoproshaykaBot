using PoproshaykaBot.WinForms.Chat;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Chat;

public record RawChatMessageReceived(ChatMessage Message, DateTimeOffset Timestamp) : EventBase;
