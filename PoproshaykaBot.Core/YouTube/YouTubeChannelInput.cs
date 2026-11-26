using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.YouTube;

public static partial class YouTubeChannelInput
{
    public static bool TryExtractDirectChannelId(string? rawInput, out string channelId)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            channelId = string.Empty;
            return false;
        }

        var trimmed = rawInput.Trim();
        if (ChannelUrlRegex().Match(trimmed) is { Success: true } urlMatch)
        {
            channelId = urlMatch.Groups["id"].Value;
            return true;
        }

        if (BareChannelIdRegex().IsMatch(trimmed))
        {
            channelId = trimmed;
            return true;
        }

        channelId = string.Empty;
        return false;
    }

    public static string? NormalizeHandle(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return null;
        }

        var trimmed = rawInput.Trim();
        if (HandleUrlRegex().Match(trimmed) is { Success: true } urlMatch)
        {
            return "@" + urlMatch.Groups["handle"].Value.TrimStart('@');
        }

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var stripped = trimmed.TrimStart('@');
        return string.IsNullOrWhiteSpace(stripped) ? null : "@" + stripped;
    }

    public static bool MatchesChannel(string? rawInput, string? channelId, string? handle)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || string.IsNullOrWhiteSpace(channelId))
        {
            return false;
        }

        var trimmed = rawInput.Trim();
        if (string.Equals(trimmed, channelId.Trim(), StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(handle)
            && string.Equals(trimmed, handle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NormalizeHandle(trimmed) is { } normalizedInput
            && NormalizeHandle(handle) is { } normalizedHandle
            && string.Equals(normalizedInput, normalizedHandle, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^https?://(?:www\.|m\.)?youtube\.com/channel/(?<id>UC[A-Za-z0-9_-]{10,})(?:[/?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ChannelUrlRegex();

    [GeneratedRegex(@"^UC[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex BareChannelIdRegex();

    [GeneratedRegex(@"^https?://(?:www\.|m\.)?youtube\.com/@(?<handle>[^/?#\s]+)(?:[/?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex HandleUrlRegex();
}
