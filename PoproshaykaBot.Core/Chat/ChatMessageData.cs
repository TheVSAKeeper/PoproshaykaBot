using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public class ChatMessageData
{
    public required StreamingPlatform Platform { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string ChannelId { get; init; } = string.Empty;
    public string? ChatSessionId { get; init; }
    public string MessageId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ChatMessageType MessageType { get; init; }
    public UserStatus Status { get; init; } = UserStatus.None;
    public bool IsFirstTime { get; init; } = false;
    public bool IsHistorical { get; init; } = false;
    public bool IsBot { get; init; } = false;
    public CommandResponseMark? CommandResponse { get; init; }
    public string Color { get; init; } = string.Empty;

    public List<EmoteInfo> Emotes { get; init; } = [];
    public List<KeyValuePair<string, string>> Badges { get; init; } = [];
    public Dictionary<string, string> BadgeUrls { get; init; } = new(StringComparer.Ordinal);
}
