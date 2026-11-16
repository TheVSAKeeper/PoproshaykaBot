using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Chat;

public sealed record ChatMessage(
    StreamingPlatform Platform,
    string Channel,
    string Id,
    string UserId,
    string Username,
    string DisplayName,
    string Message,
    IReadOnlyList<(string SetId, string BadgeId)> Badges,
    IReadOnlyList<EmoteOccurrence> Emotes,
    bool IsBroadcaster,
    bool IsModerator,
    bool IsVip,
    bool IsSubscriber,
    bool IsBot = false,
    string Color = "",
    string ChannelId = "",
    string? ChatSessionId = null);

public sealed record EmoteOccurrence(string EmoteId, string Name, int StartIndex, int EndIndex);
