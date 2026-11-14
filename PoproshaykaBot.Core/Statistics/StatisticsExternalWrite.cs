using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Statistics;

public readonly struct StatisticsExternalWrite : IEquatable<StatisticsExternalWrite>
{
    private readonly string[]? _relativePaths;

    public StatisticsExternalWrite(IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        _relativePaths = NormalizeDistinct(relativePaths);
    }

    private StatisticsExternalWrite(string[] normalizedPaths)
    {
        _relativePaths = normalizedPaths;
    }

    public static StatisticsExternalWrite None => default;

    public bool IsEmpty => _relativePaths is null or { Length: 0 };

    public IReadOnlyList<string> RelativePaths => _relativePaths ?? [];

    public static StatisticsExternalWrite Files(params ReadOnlySpan<string> relativePaths)
    {
        if (relativePaths.IsEmpty)
        {
            return None;
        }

        var list = new List<string>(relativePaths.Length);
        foreach (var path in relativePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                list.Add(LegacyDataCatalog.NormalizeRelativePath(path));
            }
        }

        if (list.Count == 0)
        {
            return None;
        }

        var unique = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Array.Sort(unique, StringComparer.OrdinalIgnoreCase);
        return new(unique);
    }

    public bool Touches(string relativePath)
    {
        if (IsEmpty || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var target = LegacyDataCatalog.NormalizeRelativePath(relativePath);
        foreach (var path in _relativePaths!)
        {
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool TouchesAny(IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (IsEmpty)
        {
            return false;
        }

        foreach (var path in relativePaths)
        {
            if (Touches(path))
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(StatisticsExternalWrite other)
    {
        var left = RelativePaths;
        var right = other.RelativePaths;
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is StatisticsExternalWrite other && Equals(other);

    public override int GetHashCode()
    {
        if (IsEmpty)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var path in _relativePaths!)
        {
            hash.Add(path, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(StatisticsExternalWrite left, StatisticsExternalWrite right) => left.Equals(right);

    public static bool operator !=(StatisticsExternalWrite left, StatisticsExternalWrite right) => !left.Equals(right);

    private static string[]? NormalizeDistinct(IEnumerable<string> relativePaths)
    {
        var unique = relativePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => LegacyDataCatalog.NormalizeRelativePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unique.Length == 0)
        {
            return null;
        }

        Array.Sort(unique, StringComparer.OrdinalIgnoreCase);
        return unique;
    }
}
