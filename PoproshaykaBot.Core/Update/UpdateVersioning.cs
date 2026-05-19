using System.Globalization;

namespace PoproshaykaBot.Core.Update;

public static class UpdateVersioning
{
    public static bool TryParseTag(string? raw, out Version version)
    {
        version = new(0, 0);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var plusIndex = trimmed.IndexOf('+', StringComparison.Ordinal);

        if (plusIndex > 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);

        if (dashIndex > 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return Version.TryParse(trimmed, out var parsed) && TryNormalize(parsed, out version);
    }

    public static string AssetSuffix(string architectureMoniker, UpdateKind kind)
    {
        return kind switch
        {
            UpdateKind.Portable => string.Create(CultureInfo.InvariantCulture, $"-{architectureMoniker}-portable.exe"),
            UpdateKind.FrameworkDependent => string.Create(CultureInfo.InvariantCulture, $"-{architectureMoniker}.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Сборка не поддерживает автообновление."),
        };
    }

    public static ReleaseAsset? SelectAsset(ReleaseInfo release, string architectureMoniker, UpdateKind kind)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (kind is not (UpdateKind.Portable or UpdateKind.FrameworkDependent))
        {
            return null;
        }

        var suffix = AssetSuffix(architectureMoniker, kind);

        return release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsNewer(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);

        return candidate > current;
    }

    private static bool TryNormalize(Version source, out Version normalized)
    {
        normalized = new(Math.Max(source.Major, 0),
            Math.Max(source.Minor, 0),
            Math.Max(source.Build, 0),
            Math.Max(source.Revision, 0));

        return true;
    }
}
