using System.Globalization;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed record GalleryOptions(
    string Directory,
    IReadOnlyList<string> Pages,
    IReadOnlyList<AppTheme> Themes,
    int Width,
    int Height,
    double Scale,
    double FontScale,
    string Element,
    IReadOnlyList<string> Unknown)
{
    public static IReadOnlyList<string> AllPages { get; } = ["overview", "users", "streams", "logs", "settings"];

    public static GalleryOptions Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var directory = defaultDirectory;
        var pages = AllPages;
        IReadOnlyList<AppTheme> themes = [AppTheme.Light, AppTheme.Dark];
        var width = AppDefaults.GalleryWidthDefault;
        var height = AppDefaults.GalleryHeightDefault;
        var scale = 1d;
        var fontScale = FontScaleManager.DefaultScale;
        var element = string.Empty;
        var unknown = new List<string>();

        var rest = args.ToList();
        var positional = true;
        var index = 0;

        while (index < rest.Count)
        {
            var key = rest[index].Trim();
            var value = index + 1 < rest.Count ? rest[index + 1].Trim() : string.Empty;
            index++;

            switch (key.ToLowerInvariant())
            {
                case "--pages":
                    pages = ParsePages(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--themes":
                    themes = ParseThemes(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--size":
                    (width, height) = ParseSize(value, width, height);
                    index++;
                    positional = false;
                    break;

                case "--element":
                    element = value;
                    index++;
                    positional = false;
                    break;

                case "--scale":
                    scale = ParseDouble(value, scale, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax);
                    index++;
                    positional = false;
                    break;

                case "--font-scale":
                    fontScale = ParseDouble(value, fontScale, FontScaleManager.MinScale, FontScaleManager.MaxScale);
                    index++;
                    positional = false;
                    break;

                default:
                    if (positional && !key.StartsWith("--", StringComparison.Ordinal))
                    {
                        directory = key;
                        positional = false;
                        break;
                    }

                    unknown.Add(key);
                    break;
            }
        }

        return new(directory, pages, themes, width, height, scale, fontScale, element, unknown);
    }

    private static IReadOnlyList<string> ParsePages(string value, List<string> unknown)
    {
        var requested = Split(value);
        var known = requested.Where(page => AllPages.Contains(page, StringComparer.OrdinalIgnoreCase)).ToList();
        unknown.AddRange(requested.Where(page => !AllPages.Contains(page, StringComparer.OrdinalIgnoreCase)));

        return known.Count > 0 ? known : AllPages;
    }

    private static IReadOnlyList<AppTheme> ParseThemes(string value, List<string> unknown)
    {
        var themes = new List<AppTheme>();

        foreach (var item in Split(value))
        {
            switch (item.ToLowerInvariant())
            {
                case AppThemes.LightKey:
                    themes.Add(AppTheme.Light);
                    break;

                case AppThemes.DarkKey:
                    themes.Add(AppTheme.Dark);
                    break;

                default:
                    unknown.Add(item);
                    break;
            }
        }

        return themes.Count > 0 ? themes : [AppTheme.Light, AppTheme.Dark];
    }

    private static (int Width, int Height) ParseSize(string value, int width, int height)
    {
        var parts = value.Split('x', 'X', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var parsedWidth)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var parsedHeight)
            || parsedWidth <= 0
            || parsedHeight <= 0)
        {
            return (width, height);
        }

        return (parsedWidth, parsedHeight);
    }

    private static double ParseDouble(string value, double fallback, double min, double max)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static IReadOnlyList<string> Split(string value)
    {
        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
