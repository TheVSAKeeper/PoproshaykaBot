using PoproshaykaBot.WinForms.Chat;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Twitch.Chat;

public sealed class EventSubChatMessageMapper
{
    public ChatMessage Map(JsonElement payload)
    {
        if (!payload.TryGetProperty("event", out var evt))
        {
            throw new ArgumentException("Payload не содержит поле 'event'", nameof(payload));
        }

        var channel = GetString(evt, "broadcaster_user_login");
        var messageId = GetString(evt, "message_id");
        var userId = GetString(evt, "chatter_user_id");
        var username = GetString(evt, "chatter_user_login");
        var displayName = GetString(evt, "chatter_user_name");

        var messageText = evt.TryGetProperty("message", out var msgEl)
                          && msgEl.TryGetProperty("text", out var textEl)
            ? textEl.GetString() ?? string.Empty
            : string.Empty;

        var (badges, isBroadcaster, isModerator, isVip, isSubscriber) = ExtractBadges(evt);
        var emotes = ExtractEmotes(evt);

        return new(channel,
            messageId,
            userId,
            username,
            displayName,
            messageText,
            badges,
            emotes,
            isBroadcaster,
            isModerator,
            isVip,
            isSubscriber);
    }

    private static (IReadOnlyList<(string SetId, string BadgeId)> Badges,
        bool IsBroadcaster, bool IsModerator, bool IsVip, bool IsSubscriber)
        ExtractBadges(JsonElement evt)
    {
        var badges = new List<(string SetId, string BadgeId)>();
        var isBroadcaster = false;
        var isModerator = false;
        var isVip = false;
        var isSubscriber = false;

        if (!evt.TryGetProperty("badges", out var badgesEl) || badgesEl.ValueKind != JsonValueKind.Array)
        {
            return (badges, isBroadcaster, isModerator, isVip, isSubscriber);
        }

        foreach (var badge in badgesEl.EnumerateArray())
        {
            var setId = badge.TryGetProperty("set_id", out var setIdEl) ? setIdEl.GetString() ?? "" : "";
            var badgeId = badge.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";

            badges.Add((setId, badgeId));

            switch (setId)
            {
                case "broadcaster":
                    isBroadcaster = true;
                    break;

                case "moderator":
                    isModerator = true;
                    break;

                case "vip":
                    isVip = true;
                    break;

                case "subscriber" or "founder":
                    isSubscriber = true;
                    break;
            }
        }

        return (badges, isBroadcaster, isModerator, isVip, isSubscriber);
    }

    private static IReadOnlyList<EmoteOccurrence> ExtractEmotes(JsonElement evt)
    {
        var emotes = new List<EmoteOccurrence>();

        if (!evt.TryGetProperty("message", out var msgEl)
            || !msgEl.TryGetProperty("fragments", out var fragmentsEl)
            || fragmentsEl.ValueKind != JsonValueKind.Array)
        {
            return emotes;
        }

        var textOffset = 0;

        foreach (var fragment in fragmentsEl.EnumerateArray())
        {
            var type = fragment.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            var fragmentText = fragment.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

            if (string.Equals(type, "emote", StringComparison.Ordinal)
                && fragment.TryGetProperty("emote", out var emoteEl)
                && emoteEl.ValueKind == JsonValueKind.Object)
            {
                var emoteId = emoteEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var startIndex = textOffset;
                var endIndex = textOffset + fragmentText.Length - 1;

                emotes.Add(new(emoteId, fragmentText, startIndex, endIndex));
            }

            textOffset += fragmentText.Length;
        }

        return emotes;
    }

    private static string GetString(JsonElement el, string propertyName)
    {
        return el.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;
    }
}
