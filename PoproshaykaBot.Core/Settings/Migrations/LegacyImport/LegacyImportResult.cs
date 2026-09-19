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

    public bool CopiedStatistics => CopiedFiles.Any(file =>
        string.Equals(file, LegacyDataCatalog.UserStatisticsFileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(file, LegacyDataCatalog.BotStatisticsFileName, StringComparison.OrdinalIgnoreCase));
}
