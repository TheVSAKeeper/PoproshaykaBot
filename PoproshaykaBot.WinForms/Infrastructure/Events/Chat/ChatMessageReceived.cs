using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Chat;

public sealed record ChatMessageReceived(
    string Channel,
    string MessageId,
    string UserId,
    string Username,
    string DisplayName,
    string Text,
    UserStatus Status,
    bool IsFirstTime,
    ChatMessageData HistoryEntry) : EventBase;
