using System.Globalization;

namespace PoproshaykaBot.Core.Update;

public static class UpdateRuntime
{
    public static int? ParseMajor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        var start = FindDigitsStart(trimmed, "Version=v");

        if (start < 0)
        {
            start = FindDigitsStart(trimmed, "net");
        }

        if (start < 0 || start >= trimmed.Length || !char.IsAsciiDigit(trimmed[start]))
        {
            return null;
        }

        var end = start;

        while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end]))
        {
            end++;
        }

        return int.TryParse(trimmed.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
            ? major
            : null;
    }

    private static int FindDigitsStart(string value, string marker)
    {
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? -1 : index + marker.Length;
    }
}
