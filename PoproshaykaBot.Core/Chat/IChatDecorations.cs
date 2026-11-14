namespace PoproshaykaBot.Core.Chat;

public interface IChatDecorations
{
    List<EmoteInfo> ExtractEmotes(ChatMessage chatMessage, int emoteSizePixels);

    Dictionary<string, string> ExtractBadgeUrls(IReadOnlyList<(string SetId, string BadgeId)> badges, int badgeSizePixels);
}

public sealed class NullChatDecorations : IChatDecorations
{
    public static readonly NullChatDecorations Instance = new();

    private NullChatDecorations()
    {
    }

    public List<EmoteInfo> ExtractEmotes(ChatMessage chatMessage, int emoteSizePixels) => [];

    public Dictionary<string, string> ExtractBadgeUrls(IReadOnlyList<(string SetId, string BadgeId)> badges, int badgeSizePixels) => [];
}
