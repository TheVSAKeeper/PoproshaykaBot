using System.Text;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal static class YouTubeMessageSplitter
{
    public const int DefaultMaxLength = 200;

    public static IReadOnlyList<string> Split(string? rawText, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        var text = NormalizeOutgoingText(rawText);

        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (text.Length <= maxLength)
        {
            return [text];
        }

        var chunks = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var remaining = text.Length - index;
            var take = Math.Min(maxLength, remaining);

            if (take < remaining)
            {
                var space = text.LastIndexOf(' ', index + take - 1, take);

                if (space > index)
                {
                    take = space - index;
                }
                else if (take > 1 && char.IsHighSurrogate(text[index + take - 1]))
                {
                    take--;
                }
            }

            var chunk = text.Substring(index, take).Trim();

            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            index += take;
        }

        return chunks;
    }

    private static string NormalizeOutgoingText(string? rawText)
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
}
