namespace PoproshaykaBot.Core.Settings.Obs;

public static class ObsPlatformBadgeMode
{
    public const string Auto = "auto";
    public const string Always = "always";
    public const string Hidden = "hidden";

    public static readonly IReadOnlyList<(string Value, string DisplayName)> Modes =
    [
        (Auto, "Авто (при мультистриме)"),
        (Always, "Всегда показывать"),
        (Hidden, "Скрыть"),
    ];

    public static readonly IReadOnlyList<string> AllValues = [Auto, Always, Hidden];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Auto;
        }

        var trimmed = value.Trim();

        if (string.Equals(trimmed, Always, StringComparison.OrdinalIgnoreCase))
        {
            return Always;
        }

        if (string.Equals(trimmed, Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return Hidden;
        }

        return Auto;
    }

    public static bool ResolveShowBadge(string? mode, int enabledPlatformCount)
    {
        return Normalize(mode) switch
        {
            Always => true,
            Hidden => false,
            _ => enabledPlatformCount > 1,
        };
    }
}
