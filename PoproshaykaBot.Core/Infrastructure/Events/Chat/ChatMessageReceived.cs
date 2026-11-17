using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Infrastructure.Events.Chat;

public sealed record ChatMessageReceived(
    StreamingPlatform Platform,
    string Channel,
    string MessageId,
    string UserId,
    string Username,
    string DisplayName,
    string Text,
    UserStatus Status,
    bool IsFirstTime,
    ChatMessageData HistoryEntry,
    bool IsBot = false,
    CommandResponseMark? CommandResponse = null,
    bool IsReplacement = false,
    ChatOrigin? Origin = null,
    bool? RecordsUserData = null) : EventBase, IPlatformEvent
{
    public PlatformUserKey SenderKey => new(Platform, UserId);
    public ChannelKey? ChannelKey => Origin?.Channel ?? Platforms.ChannelKey.FromId(Platform, HistoryEntry.ChannelId);
}
