using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using System.Globalization;
using System.Text;

namespace PoproshaykaBot.Core.Twitch.Chat;

public static class TwitchIrcMessageParser
{
    private const string ActionPrefix = "\u0001ACTION ";

    public static bool TryParsePrivMsg(
        string rawLine,
        string fallbackChannel,
        string fallbackChannelId,
        string? botUserId,
        out ChatMessage message,
        out DateTimeOffset timestamp)
    {
        message = null!;
        timestamp = default;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var span = rawLine.AsSpan().Trim();
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);

        if (span.Length > 0 && span[0] == '@')
        {
            var tagEnd = span.IndexOf(' ');
            if (tagEnd <= 0)
            {
                return false;
            }

            ParseTags(span[1..tagEnd], tags);
            span = span[(tagEnd + 1)..].TrimStart();
        }

        var username = string.Empty;
        if (span.Length > 0 && span[0] == ':')
        {
            var prefixEnd = span.IndexOf(' ');
            if (prefixEnd <= 0)
            {
                return false;
            }

            var prefix = span[1..prefixEnd];
            var bang = prefix.IndexOf('!');
            username = (bang > 0 ? prefix[..bang] : prefix).ToString().Trim().ToLowerInvariant();
            span = span[(prefixEnd + 1)..].TrimStart();
        }

        var cmdEnd = span.IndexOf(' ');
        if (cmdEnd <= 0)
        {
            return false;
        }

        var command = span[..cmdEnd];
        if (!command.Equals("PRIVMSG", StringComparison.Ordinal))
        {
            return false;
        }

        if (tags.TryGetValue("rm-deleted", out var rmDeleted) && string.Equals(rmDeleted, "1", StringComparison.Ordinal))
        {
            return false;
        }

        span = span[(cmdEnd + 1)..].TrimStart();
        var channelEnd = span.IndexOf(' ');
        if (channelEnd <= 0)
        {
            return false;
        }

        var channelRaw = span[..channelEnd].TrimStart('#').ToString();
        var channel = string.IsNullOrWhiteSpace(channelRaw) ? fallbackChannel : channelRaw;

        var textSpan = span[(channelEnd + 1)..].TrimStart();
        if (textSpan.Length > 0 && textSpan[0] == ':')
        {
            textSpan = textSpan[1..];
        }

        var text = textSpan.ToString();
        if (text.StartsWith(ActionPrefix, StringComparison.Ordinal) && text.EndsWith('\u0001'))
        {
            text = text[ActionPrefix.Length..^1];
        }

        var messageId = tags.GetValueOrDefault("id", string.Empty).Trim();
        if (string.IsNullOrEmpty(messageId))
        {
            return false;
        }

        if (!tags.TryGetValue("tmi-sent-ts", out var rawTs)
            || !long.TryParse(rawTs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMs)
            || unixMs <= 0
            || unixMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

        var userId = tags.GetValueOrDefault("user-id", string.Empty).Trim();
        var displayName = tags.GetValueOrDefault("display-name", string.Empty).Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = string.IsNullOrEmpty(username) ? userId : username;
        }

        var roomId = tags.GetValueOrDefault("room-id", string.Empty).Trim();
        if (!string.IsNullOrEmpty(roomId)
            && !string.IsNullOrWhiteSpace(fallbackChannelId)
            && !string.Equals(roomId, fallbackChannelId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var channelId = string.IsNullOrEmpty(roomId) ? fallbackChannelId : roomId;

        var color = NormalizeColor(tags.GetValueOrDefault("color", string.Empty));
        var (badges, isBroadcaster, isModerator, isVip, isSubscriber) = ParseBadges(tags);
        var emotes = ParseEmotes(tags.GetValueOrDefault("emotes", string.Empty), text);

        var isBot = !string.IsNullOrEmpty(botUserId)
            && !string.IsNullOrEmpty(userId)
            && string.Equals(userId, botUserId, StringComparison.Ordinal);

        message = new ChatMessage(
            StreamingPlatform.Twitch,
            channel,
            messageId,
            userId,
            username,
            displayName,
            text,
            badges,
            emotes,
            isBroadcaster,
            isModerator,
            isVip,
            isSubscriber,
            isBot,
            color,
            channelId);

        return true;
    }

    private static void ParseTags(ReadOnlySpan<char> tagBlock, Dictionary<string, string> target)
    {
        while (!tagBlock.IsEmpty)
        {
            var semi = tagBlock.IndexOf(';');
            var entry = semi < 0 ? tagBlock : tagBlock[..semi];
            var eq = entry.IndexOf('=');
            if (eq > 0)
            {
                var key = entry[..eq].ToString();
                var value = UnescapeTagValue(entry[(eq + 1)..]);
                target[key] = value;
            }

            if (semi < 0)
            {
                break;
            }

            tagBlock = tagBlock[(semi + 1)..];
        }
    }

    private static string UnescapeTagValue(ReadOnlySpan<char> value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value.ToString();
        }

        var sb = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i + 1] switch
                {
                    's' => ' ',
                    ':' => ';',
                    '\\' => '\\',
                    'r' => '\r',
                    'n' => '\n',
                    var other => other,
                });
                i += 2;
            }
            else
            {
                sb.Append(value[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static (IReadOnlyList<(string SetId, string BadgeId)> Badges,
        bool IsBroadcaster, bool IsModerator, bool IsVip, bool IsSubscriber)
        ParseBadges(Dictionary<string, string> tags)
    {
        var badges = new List<(string SetId, string BadgeId)>();
        var isBroadcaster = false;
        var isModerator = tags.GetValueOrDefault("mod") == "1";
        var isVip = false;
        var isSubscriber = tags.GetValueOrDefault("subscriber") == "1";

        if (tags.TryGetValue("badges", out var rawBadges) && !string.IsNullOrWhiteSpace(rawBadges))
        {
            foreach (var part in rawBadges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var slash = part.IndexOf('/');
                if (slash <= 0 || slash >= part.Length - 1)
                {
                    continue;
                }

                var setId = part[..slash];
                var badgeId = part[(slash + 1)..];
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
        }

        return (badges, isBroadcaster, isModerator, isVip, isSubscriber);
    }

    private static IReadOnlyList<EmoteOccurrence> ParseEmotes(string rawEmotes, string text)
    {
        if (string.IsNullOrWhiteSpace(rawEmotes) || string.IsNullOrEmpty(text))
        {
            return [];
        }

        var codePointToUtf16 = BuildCodePointToUtf16Map(text);
        var codePointCount = codePointToUtf16.Count;
        var result = new List<EmoteOccurrence>();
        foreach (var group in rawEmotes.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = group.IndexOf(':');
            if (colon <= 0 || colon >= group.Length - 1)
            {
                continue;
            }

            var emoteId = group[..colon];
            var positions = group[(colon + 1)..];
            foreach (var range in positions.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = range.IndexOf('-');
                if (dash <= 0 || dash >= range.Length - 1)
                {
                    continue;
                }

                if (!int.TryParse(range[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startCp)
                    || !int.TryParse(range[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var endCp))
                {
                    continue;
                }

                if (startCp < 0 || endCp < startCp || endCp >= codePointCount)
                {
                    continue;
                }

                var startUtf16 = codePointToUtf16[startCp];
                var endUtf16 = endCp + 1 < codePointCount ? codePointToUtf16[endCp + 1] - 1 : text.Length - 1;
                if (startUtf16 < 0 || endUtf16 < startUtf16 || endUtf16 >= text.Length)
                {
                    continue;
                }

                var name = text.Substring(startUtf16, endUtf16 - startUtf16 + 1);
                result.Add(new(emoteId, name, startUtf16, endUtf16));
            }
        }

        result.Sort(static (left, right) => left.StartIndex.CompareTo(right.StartIndex));
        return result;
    }

    private static List<int> BuildCodePointToUtf16Map(string text)
    {
        var map = new List<int>(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            map.Add(index);
            index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;
        }

        return map;
    }

    private static string NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed is not ['#', _, _, _, _, _, _])
        {
            return string.Empty;
        }

        for (var i = 1; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch is (< '0' or > '9') and (< 'a' or > 'f') and (< 'A' or > 'F'))
            {
                return string.Empty;
            }
        }

        return trimmed;
    }
}
