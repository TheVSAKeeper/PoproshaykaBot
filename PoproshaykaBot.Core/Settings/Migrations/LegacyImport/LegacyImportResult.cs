using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

public sealed class LegacyImportResult
{
    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required IReadOnlyList<string> CopiedFiles { get; init; }

    public required IReadOnlyList<LegacyImportSkip> SkippedFiles { get; init; }

    public required IReadOnlyList<LegacyImportFailure> Failures { get; init; }

    public required bool RequiresReauthorization { get; init; }

    public bool IsInPlaceMigration { get; init; }

    public IReadOnlyList<string> UnmigratedLegacyFiles { get; init; } = [];

    public bool CopiedStatistics => Copied(LegacyDataCatalog.UserStatisticsFileName)
        || Copied(LegacyDataCatalog.BotStatisticsFileName);

    public bool CopiedStreamHistory => Copied(LegacyDataCatalog.StreamSessionsFileName);

    public StatisticsExternalWrite ExternalWrite => new(CopiedStatistics, CopiedStreamHistory);

    private bool Copied(string fileName)
    {
        return CopiedFiles.Any(file => string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase));
    }
}
