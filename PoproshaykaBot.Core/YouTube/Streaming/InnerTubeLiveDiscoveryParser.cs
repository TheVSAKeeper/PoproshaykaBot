using System.Globalization;
using System.Text.Json;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal static class InnerTubeLiveDiscoveryParser
{
    private const int MaxPlayerResponseLength = 4 * 1024 * 1024;
    private const string PlayerResponseMarker = "ytInitialPlayerResponse";
    private const string InitialDataMarker = "ytInitialData";

    public static YouTubePublicLiveVideo? ParseChannelLivePage(string html, string expectedChannelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedChannelId);
        var normalizedExpectedChannelId = expectedChannelId.Trim();

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Transient,
                "Страница трансляции YouTube вернула пустой ответ.");
        }

        var scriptBlocks = EnumerateScriptBlocks(html);
        var remainingScanBudget = Math.Max(html.Length, 1);

        using var document = TryExtractPlayerResponseDocument(html, scriptBlocks, normalizedExpectedChannelId, ref remainingScanBudget);
        if (document is null)
        {
            if (InnerTubeLiveChatParser.IsConsentPage(html))
            {
                throw new YouTubeApiException(
                    YouTubeApiErrorKind.Transient,
                    "YouTube потребовал подтверждение consent при поиске активного эфира.");
            }

            if (IsVerifiedChannelBrowsePage(html, scriptBlocks, normalizedExpectedChannelId, ref remainingScanBudget))
            {
                return null;
            }

            throw new YouTubeApiException(
                YouTubeApiErrorKind.Transient,
                "Неожиданный ответ страницы канала YouTube при поиске активного эфира.");
        }

        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryGetProperty(root, "playabilityStatus", out var playabilityStatus))
            {
                var status = GetStringProperty(playabilityStatus, "status");
                if (string.Equals(status, "LIVE_STREAM_OFFLINE", StringComparison.Ordinal))
                {
                    return null;
                }

                if (string.Equals(status, "LOGIN_REQUIRED", StringComparison.Ordinal))
                {
                    var reason = GetStringProperty(playabilityStatus, "reason");
                    if (!string.IsNullOrWhiteSpace(reason)
                        && (reason.Contains("Private video", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("This video is private", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    throw new YouTubeApiException(
                        YouTubeApiErrorKind.Transient,
                        "YouTube потребовал проверку подтверждения при поиске активного эфира.");
                }

                if (string.Equals(status, "UNPLAYABLE", StringComparison.Ordinal)
                    || string.Equals(status, "ERROR", StringComparison.Ordinal))
                {
                    return null;
                }
            }

            if (!TryGetProperty(root, "videoDetails", out var videoDetails))
            {
                return null;
            }

            var videoId = GetStringProperty(videoDetails, "videoId");
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return null;
            }

            var channelId = GetStringProperty(videoDetails, "channelId");
            if (string.IsNullOrWhiteSpace(channelId)
                || !string.Equals(channelId.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
            {
                return null;
            }

            if (GetBooleanProperty(videoDetails, "isReplay") == true
                || GetBooleanProperty(videoDetails, "isUpcoming") == true)
            {
                return null;
            }

            var isLive = GetBooleanProperty(videoDetails, "isLive");
            var title = GetStringProperty(videoDetails, "title");
            var author = GetStringProperty(videoDetails, "author");

            bool? isLiveNow = null;
            DateTimeOffset? startTimestamp = null;

            if (TryGetProperty(root, "microformat", out var microformat)
                && TryGetProperty(microformat, "playerMicroformatRenderer", out var renderer)
                && TryGetProperty(renderer, "liveBroadcastDetails", out var liveDetails))
            {
                isLiveNow = GetBooleanProperty(liveDetails, "isLiveNow");
                startTimestamp = GetDateTimeOffsetProperty(liveDetails, "startTimestamp");

                var endTimestamp = GetStringProperty(liveDetails, "endTimestamp");
                if (!string.IsNullOrWhiteSpace(endTimestamp))
                {
                    return null;
                }

                if (isLiveNow == false)
                {
                    return null;
                }
            }

            var activelyLive = isLiveNow == true || (isLiveNow is null && isLive == true);
            if (!activelyLive)
            {
                return null;
            }

            return new YouTubePublicLiveVideo(
                videoId.Trim(),
                normalizedExpectedChannelId,
                author?.Trim() ?? string.Empty,
                title?.Trim() ?? string.Empty,
                startTimestamp?.ToUniversalTime());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Transient,
                "Не удалось прочитать данные трансляции со страницы YouTube.",
                innerException: ex);
        }
    }

    private static bool IsVerifiedChannelBrowsePage(
        string html,
        IReadOnlyList<(int Start, int Length)> scriptBlocks,
        string normalizedExpectedChannelId,
        ref int remainingScanBudget)
    {
        var matched = false;
        var budget = remainingScanBudget;

        ScanScriptObjectAssignments(
            html,
            scriptBlocks,
            InitialDataMarker,
            ref budget,
            doc =>
            {
                try
                {
                    if (IsChannelBrowseData(doc.RootElement, normalizedExpectedChannelId))
                    {
                        matched = true;
                        return true;
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                }

                return false;
            });

        remainingScanBudget = budget;
        return matched;
    }

    private static bool IsChannelBrowseData(JsonElement root, string normalizedExpectedChannelId)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetProperty(root, "metadata", out var metadata)
            && TryGetProperty(metadata, "channelMetadataRenderer", out var channelMetadata))
        {
            var externalId = GetStringProperty(channelMetadata, "externalId");
            if (string.Equals(externalId?.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (TryGetProperty(root, "microformat", out var microformat)
            && TryGetProperty(microformat, "microformatDataRenderer", out var microformatRenderer))
        {
            var urlCanonical = GetStringProperty(microformatRenderer, "urlCanonical");
            if (!string.IsNullOrWhiteSpace(urlCanonical)
                && urlCanonical.Trim().EndsWith($"/channel/{normalizedExpectedChannelId}", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (TryGetProperty(root, "header", out var header))
        {
            if (TryGetProperty(header, "c4TabbedHeaderRenderer", out var c4Header)
                && string.Equals(GetStringProperty(c4Header, "channelId")?.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
            {
                return true;
            }

            if (TryGetProperty(header, "pageHeaderRenderer", out var pageHeader))
            {
                if (string.Equals(GetStringProperty(pageHeader, "channelId")?.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (TryGetProperty(pageHeader, "content", out var content)
                    && TryGetProperty(content, "pageHeaderViewModel", out var viewModel)
                    && string.Equals(GetStringProperty(viewModel, "channelId")?.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static JsonDocument? TryExtractPlayerResponseDocument(
        string html,
        IReadOnlyList<(int Start, int Length)> scriptBlocks,
        string normalizedExpectedChannelId,
        ref int remainingScanBudget)
    {
        JsonDocument? matchingDoc = null;
        JsonDocument? fallbackDoc = null;
        var budget = remainingScanBudget;

        var sawMalformedCandidate = ScanScriptObjectAssignments(
            html,
            scriptBlocks,
            PlayerResponseMarker,
            ref budget,
            doc =>
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || (!root.TryGetProperty("videoDetails", out var videoDetails)
                        && !root.TryGetProperty("playabilityStatus", out _)))
                {
                    return false;
                }

                try
                {
                    if (root.TryGetProperty("videoDetails", out videoDetails)
                        && string.Equals(GetStringProperty(videoDetails, "channelId")?.Trim(), normalizedExpectedChannelId, StringComparison.Ordinal))
                    {
                        matchingDoc = doc;
                        return true;
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                }

                if (fallbackDoc is null)
                {
                    fallbackDoc = doc;
                    return false;
                }

                return false;
            },
            takeOwnershipWhenKept: doc => ReferenceEquals(doc, matchingDoc) || ReferenceEquals(doc, fallbackDoc));

        remainingScanBudget = budget;

        if (matchingDoc is not null)
        {
            fallbackDoc?.Dispose();
            return matchingDoc;
        }

        if (fallbackDoc is not null)
        {
            return fallbackDoc;
        }

        if (sawMalformedCandidate)
        {
            throw new YouTubeApiException(
                YouTubeApiErrorKind.Transient,
                "Не удалось корректно разобрать ytInitialPlayerResponse со страницы YouTube.");
        }

        return null;
    }

    private static bool ScanScriptObjectAssignments(
        string html,
        IReadOnlyList<(int Start, int Length)> scriptBlocks,
        string marker,
        ref int remainingScanBudget,
        Func<JsonDocument, bool> onParsedObject,
        Func<JsonDocument, bool>? takeOwnershipWhenKept = null)
    {
        var sawMalformedCandidate = false;

        foreach (var (blockStart, blockLength) in scriptBlocks)
        {
            var blockEnd = blockStart + blockLength;
            var searchStart = blockStart;

            while (searchStart < blockEnd && remainingScanBudget > 0)
            {
                var markerIndex = html.IndexOf(marker, searchStart, blockEnd - searchStart, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    break;
                }

                if (!HasValidAssignmentPrefix(html, blockStart, markerIndex, out var quoteChar))
                {
                    searchStart = markerIndex + marker.Length;
                    continue;
                }

                var cursor = markerIndex + marker.Length;

                if (quoteChar is { } expectedQuote)
                {
                    if (cursor >= blockEnd || html[cursor] != expectedQuote)
                    {
                        searchStart = markerIndex + marker.Length;
                        continue;
                    }

                    cursor++;
                    while (cursor < blockEnd && char.IsWhiteSpace(html[cursor]))
                    {
                        cursor++;
                    }

                    if (cursor >= blockEnd || html[cursor] != ']')
                    {
                        searchStart = markerIndex + marker.Length;
                        continue;
                    }

                    cursor++;
                }
                else if (cursor < blockEnd && !char.IsWhiteSpace(html[cursor]) && html[cursor] != '=')
                {
                    searchStart = markerIndex + marker.Length;
                    continue;
                }

                while (cursor < blockEnd && char.IsWhiteSpace(html[cursor]))
                {
                    cursor++;
                }

                if (cursor >= blockEnd || html[cursor] != '=')
                {
                    searchStart = markerIndex + marker.Length;
                    continue;
                }

                cursor++;
                while (cursor < blockEnd && char.IsWhiteSpace(html[cursor]))
                {
                    cursor++;
                }

                if (cursor >= blockEnd || html[cursor] != '{')
                {
                    searchStart = markerIndex + marker.Length;
                    continue;
                }

                if (!TryExtractBalancedJsonObject(html, cursor, blockEnd, ref remainingScanBudget, out var extracted, out var endIndex))
                {
                    sawMalformedCandidate = true;
                    searchStart = Math.Max(cursor + 1, endIndex + 1);
                    continue;
                }

                JsonDocument? doc = null;
                var keepOwnership = false;
                try
                {
                    doc = JsonDocument.Parse(extracted!);
                    var stop = onParsedObject(doc);
                    keepOwnership = takeOwnershipWhenKept?.Invoke(doc) == true;
                    if (stop)
                    {
                        return sawMalformedCandidate;
                    }
                }
                catch (JsonException)
                {
                    sawMalformedCandidate = true;
                }
                finally
                {
                    if (!keepOwnership)
                    {
                        doc?.Dispose();
                    }
                }

                searchStart = endIndex + 1;
            }
        }

        return sawMalformedCandidate;
    }

    private static List<(int Start, int Length)> EnumerateScriptBlocks(string html)
    {
        var blocks = new List<(int Start, int Length)>();
        var pos = 0;

        while (pos < html.Length)
        {
            var ltIndex = html.IndexOf('<', pos);
            if (ltIndex < 0)
            {
                break;
            }

            if (ltIndex + 4 <= html.Length
                && string.Compare(html, ltIndex, "<!--", 0, 4, StringComparison.Ordinal) == 0)
            {
                var commentEnd = html.IndexOf("-->", ltIndex + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    break;
                }

                pos = commentEnd + 3;
                continue;
            }

            if (ltIndex + 7 <= html.Length
                && string.Compare(html, ltIndex, "<script", 0, 7, StringComparison.OrdinalIgnoreCase) == 0
                && (ltIndex + 7 == html.Length
                    || char.IsWhiteSpace(html[ltIndex + 7])
                    || html[ltIndex + 7] is '>' or '/'))
            {
                var tagCursor = ltIndex + 7;
                char? attrQuote = null;
                var tagClose = -1;
                var lastNonWhitespace = '\0';

                while (tagCursor < html.Length)
                {
                    var ch = html[tagCursor];
                    if (attrQuote is { } activeQuote)
                    {
                        if (ch == activeQuote)
                        {
                            attrQuote = null;
                        }
                    }
                    else if (ch is '"' or '\'')
                    {
                        attrQuote = ch;
                        lastNonWhitespace = ch;
                    }
                    else if (ch == '>')
                    {
                        tagClose = tagCursor;
                        break;
                    }
                    else if (!char.IsWhiteSpace(ch))
                    {
                        lastNonWhitespace = ch;
                    }

                    tagCursor++;
                }

                if (tagClose < 0)
                {
                    break;
                }

                if (lastNonWhitespace == '/')
                {
                    pos = tagClose + 1;
                    continue;
                }

                var contentStart = tagClose + 1;
                var closeSearch = contentStart;
                var contentEnd = -1;
                var nextPos = html.Length;

                while (closeSearch < html.Length)
                {
                    var closeCandidate = html.IndexOf("</script", closeSearch, StringComparison.OrdinalIgnoreCase);
                    if (closeCandidate < 0)
                    {
                        break;
                    }

                    var afterTag = closeCandidate + 8;
                    if (afterTag < html.Length && (char.IsWhiteSpace(html[afterTag]) || html[afterTag] == '>'))
                    {
                        var closeEnd = html.IndexOf('>', afterTag);
                        if (closeEnd >= 0)
                        {
                            contentEnd = closeCandidate;
                            nextPos = closeEnd + 1;
                            break;
                        }
                    }

                    closeSearch = closeCandidate + 8;
                }

                if (contentEnd < 0)
                {
                    blocks.Add((contentStart, html.Length - contentStart));
                    break;
                }

                if (contentEnd > contentStart)
                {
                    blocks.Add((contentStart, contentEnd - contentStart));
                }

                pos = nextPos;
                continue;
            }

            pos = ltIndex + 1;
        }

        return blocks;
    }

    private static bool HasValidAssignmentPrefix(string html, int blockStart, int markerIndex, out char? quoteChar)
    {
        if (markerIndex == blockStart)
        {
            quoteChar = null;
            return true;
        }

        var prev = html[markerIndex - 1];
        if (prev is '"' or '\'')
        {
            var i = markerIndex - 2;
            while (i >= blockStart && char.IsWhiteSpace(html[i]))
            {
                i--;
            }

            if (i >= blockStart && html[i] == '[')
            {
                i--;
                while (i >= blockStart && char.IsWhiteSpace(html[i]))
                {
                    i--;
                }

                if (EndsWithWindowIdentifier(html, blockStart, i))
                {
                    quoteChar = prev;
                    return true;
                }
            }

            quoteChar = null;
            return false;
        }

        if (prev == '.')
        {
            quoteChar = null;
            return EndsWithWindowIdentifier(html, blockStart, markerIndex - 2);
        }

        if (char.IsWhiteSpace(prev) || prev == ';')
        {
            quoteChar = null;
            return true;
        }

        quoteChar = null;
        return false;
    }

    private static bool EndsWithWindowIdentifier(string html, int blockStart, int endIndex)
    {
        const string windowIdent = "window";
        var startIndex = endIndex - windowIdent.Length + 1;
        if (startIndex < blockStart)
        {
            return false;
        }

        if (string.Compare(html, startIndex, windowIdent, 0, windowIdent.Length, StringComparison.Ordinal) != 0)
        {
            return false;
        }

        if (startIndex == blockStart)
        {
            return true;
        }

        var before = html[startIndex - 1];
        return !char.IsLetterOrDigit(before) && before is not ('_' or '$' or '.');
    }

    private static bool TryExtractBalancedJsonObject(
        string html,
        int objectStartIndex,
        int blockEnd,
        ref int remainingScanBudget,
        out string? json,
        out int endIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var maxEnd = Math.Min(blockEnd, objectStartIndex + MaxPlayerResponseLength);
        var budgetEnd = (int)Math.Min(maxEnd, (long)objectStartIndex + remainingScanBudget);

        for (var i = objectStartIndex; i < budgetEnd; i++)
        {
            var ch = html[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    remainingScanBudget -= i - objectStartIndex + 1;
                    endIndex = i;
                    json = html.Substring(objectStartIndex, i - objectStartIndex + 1);
                    return true;
                }
            }
        }

        remainingScanBudget -= Math.Max(0, budgetEnd - objectStartIndex);
        endIndex = budgetEnd - 1;
        json = null;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBooleanProperty(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static DateTimeOffset? GetDateTimeOffsetProperty(JsonElement element, string name)
    {
        var raw = GetStringProperty(element, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
