namespace PoproshaykaBot.Core.Server.Images;

public static class MessageImageLinkDetector
{
    private const string HttpsPrefix = "https://";
    private const char NoBreakSpace = ' ';

    private static readonly char[] TokenSeparators = [' ', '\t', '\r', '\n', NoBreakSpace];
    private static readonly char[] TrailingNoise = ['.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '>', '«', '»'];
    private static readonly char[] LeadingNoise = ['(', '[', '{', '"', '\'', '<', '«'];

    public static bool TryFindFirst(string? message, IReadOnlyCollection<string>? allowedHosts, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        foreach (var token in message.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.TrimStart(LeadingNoise).TrimEnd(TrailingNoise);

            if (!trimmed.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MessageImageUrlValidator.IsAllowed(trimmed, allowedHosts, out uri))
            {
                return true;
            }
        }

        return false;
    }
}
