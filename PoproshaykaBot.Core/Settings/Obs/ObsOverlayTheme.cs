using System.Collections.Immutable;

namespace PoproshaykaBot.Core.Settings.Obs;

public static class ObsOverlayTheme
{
    public const string Classic = "classic";
    public const string Flow = "flow";

    public static readonly (string Value, string DisplayName)[] Themes =
    [
        (Classic, "Классический"),
        (Flow, "Обтекаемый (Flow)"),
    ];

    public static readonly ImmutableArray<string> Values =
        [..Themes.Select(theme => theme.Value)];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Classic;
        }

        return string.Equals(value.Trim(), Flow, StringComparison.OrdinalIgnoreCase)
            ? Flow
            : Classic;
    }
}
