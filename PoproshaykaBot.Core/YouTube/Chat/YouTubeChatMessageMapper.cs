using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using System.Text;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal enum YouTubeChatMapKind
{
    None = 0,
    ChatMessage = 1,
    IgnoredMonetizationOrMembership = 2,
    IgnoredControlEvent = 3,
}

internal sealed record YouTubeChatMapResult(
    YouTubeChatMapKind Kind,
    ChatMessage? Message = null,
    string? DiagnosticEventType = null,
    string? DiagnosticSummary = null);

internal sealed class YouTubeChatMessageMapper(IYouTubeSentMessageTracker? sentMessageTracker = null)
{
    private readonly IYouTubeSentMessageTracker _sentMessageTracker =
        sentMessageTracker ?? NullYouTubeSentMessageTracker.Instance;

    public YouTubeChatMapResult Map(
        YouTubeLiveChatItem item,
        string channel,
        string? botChannelId,
        string? broadcasterChannelId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (IsMonetizationOrMembership(item.Kind))
        {
            return new(
                YouTubeChatMapKind.IgnoredMonetizationOrMembership,
                DiagnosticEventType: item.RawEventType,
                DiagnosticSummary: string.IsNullOrWhiteSpace(item.Author.ChannelId)
                    ? item.RawEventType
                    : $"{item.RawEventType}:{item.Author.ChannelId}");
        }

        if (item.Kind != YouTubeLiveChatItemKind.TextMessage)
        {
            return new(
                YouTubeChatMapKind.IgnoredControlEvent,
                DiagnosticEventType: item.RawEventType);
        }

        if (string.IsNullOrWhiteSpace(item.Author.ChannelId))
        {
            return new(YouTubeChatMapKind.IgnoredControlEvent, DiagnosticEventType: "missingAuthorChannelId");
        }

        var rawText = string.IsNullOrWhiteSpace(item.MessageText)
            ? item.DisplayMessage
            : item.MessageText;

        var normalizedText = NormalizeMessageText(rawText);

        if (string.IsNullOrEmpty(normalizedText))
        {
            return new(YouTubeChatMapKind.IgnoredControlEvent, DiagnosticEventType: "emptyTextMessage");
        }

        var displayName = string.IsNullOrWhiteSpace(item.Author.DisplayName)
            ? item.Author.ChannelId
            : item.Author.DisplayName.Trim();

        var username = NormalizeUsername(displayName);
        var badges = BuildBadges(item.Author);

        var isDedicatedBot = !string.IsNullOrWhiteSpace(botChannelId)
            && !string.Equals(botChannelId, broadcasterChannelId, StringComparison.Ordinal);

        var isBot = _sentMessageTracker.Contains(item.Id)
            || (isDedicatedBot
                && !item.Author.IsChatOwner
                && string.Equals(item.Author.ChannelId, botChannelId, StringComparison.Ordinal));

        var message = new ChatMessage(
            StreamingPlatform.YouTube,
            channel,
            item.Id,
            item.Author.ChannelId,
            username,
            displayName,
            normalizedText,
            badges,
            [],
            item.Author.IsChatOwner,
            item.Author.IsChatModerator,
            false,
            item.Author.IsChatSponsor,
            isBot);

        return new(YouTubeChatMapKind.ChatMessage, message);
    }

    internal static string NormalizeMessageText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawText.Length);

        foreach (var ch in rawText)
        {
            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                builder.Append(' ');
                continue;
            }

            if (ch < ' ' && ch != '\t')
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static bool IsMonetizationOrMembership(YouTubeLiveChatItemKind kind)
    {
        return kind is YouTubeLiveChatItemKind.SuperChat
            or YouTubeLiveChatItemKind.SuperSticker
            or YouTubeLiveChatItemKind.FanFunding
            or YouTubeLiveChatItemKind.NewSponsor
            or YouTubeLiveChatItemKind.MemberMilestoneChat
            or YouTubeLiveChatItemKind.MembershipGifting
            or YouTubeLiveChatItemKind.GiftMembershipReceived
            or YouTubeLiveChatItemKind.Gift;
    }

    private static string NormalizeUsername(string displayName)
    {
        if (displayName.StartsWith('@') && !displayName.Contains(' ', StringComparison.Ordinal))
        {
            var stripped = displayName[1..].Trim();
            return string.IsNullOrEmpty(stripped) ? displayName : stripped;
        }

        return displayName;
    }

    private static IReadOnlyList<(string SetId, string BadgeId)> BuildBadges(YouTubeLiveChatAuthor author)
    {
        var badges = new List<(string, string)>(3);

        if (author.IsChatOwner)
        {
            badges.Add(("broadcaster", "1"));
        }

        if (author.IsChatModerator)
        {
            badges.Add(("moderator", "1"));
        }

        if (author.IsChatSponsor)
        {
            badges.Add(("subscriber", "1"));
        }

        return badges;
    }
}
