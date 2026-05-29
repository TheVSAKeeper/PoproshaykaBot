namespace PoproshaykaBot.Core.Chat;

public sealed record ChatMessage(
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
    string Color = "");

public sealed record EmoteOccurrence(string EmoteId, string Name, int StartIndex, int EndIndex);
