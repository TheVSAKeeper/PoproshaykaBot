namespace PoproshaykaBot.Core.Update;

public static class UpdateRepository
{
    private const int MaxPartLength = 100;

    public static bool IsValidSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        var separator = slug.IndexOf('/', StringComparison.Ordinal);

        if (separator <= 0 || separator != slug.LastIndexOf('/'))
        {
            return false;
        }

        var owner = slug.AsSpan(0, separator);
        var repo = slug.AsSpan(separator + 1);

        return IsValidPart(owner) && IsValidPart(repo);
    }

    public static string Resolve(string? overrideSlug, string defaultSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSlug);

        var trimmed = overrideSlug?.Trim();

        return IsValidSlug(trimmed) ? trimmed! : defaultSlug;
    }

    private static bool IsValidPart(ReadOnlySpan<char> part)
    {
        if (part.IsEmpty || part.Length > MaxPartLength || part.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in part)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
