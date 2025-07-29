namespace PoproshaykaBot.WinForms.Models;

public class ChatMessageData
{
    public DateTime Timestamp { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ChatMessageType MessageType { get; init; }
    public UserStatus Status { get; init; } = UserStatus.None;
}
