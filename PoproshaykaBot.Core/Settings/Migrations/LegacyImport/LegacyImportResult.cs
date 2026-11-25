using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

public sealed class LegacyImportResult
{
    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required IReadOnlyList<string> CopiedFiles { get; init; }

    public required IReadOnlyList<LegacyImportSkip> SkippedFiles { get; init; }

    public required IReadOnlyList<LegacyImportFailure> Failures { get; init; }

    public bool RequiresReauthorization => BotAuthorizationRequired || BroadcasterAuthorizationRecommended;

    public bool BotAuthorizationRequired { get; init; }

    public bool BroadcasterAuthorizationRecommended { get; init; }

    public bool IsInPlaceMigration { get; init; }

    public IReadOnlyList<string> UnmigratedLegacyFiles { get; init; } = [];

    public IReadOnlyList<string> MigratedSettingsFiles { get; init; } = [];

    public bool HasChanges => CopiedFiles.Count > 0 || MigratedSettingsFiles.Count > 0;

    public bool CopiedTwitchUserStatistics => Copied(LegacyDataCatalog.UserStatisticsFileName);

    public bool CopiedYouTubeUserStatistics => Copied($"{LegacyDataCatalog.YouTubeFolderName}/{LegacyDataCatalog.UserStatisticsFileName}");

    public bool CopiedYouTubeQuota => Copied(LegacyDataCatalog.YouTubeQuotaFileName);

    public bool CopiedStatistics => CopiedAny(LegacyDataCatalog.GetRelativePaths(LegacyDataCategory.UserStatistics))
        || CopiedAny(LegacyDataCatalog.GetRelativePaths(LegacyDataCategory.BotStatistics));

    public bool CopiedStreamHistory => Copied(LegacyDataCatalog.BroadcastsFileName)
        || Copied(LegacyDataCatalog.StreamSessionsFileName)
        || Copied(LegacyDataCatalog.ActiveBroadcastFileName);

    public bool CopiedPollHistory => Copied(LegacyDataCatalog.PollsHistoryFileName);

    public bool CopiedChatHistory => CopiedInDirectory(LegacyDataCatalog.ChatHistoryFolderName);

    public static bool IsChatHistoryPath(string relativePath) => LegacyDataCatalog.IsChatHistoryPath(relativePath);

    public IReadOnlyList<string> CopiedSettingsFiles =>
        [.. LegacyDataCatalog.StoreOwnedSettingsFiles.Where(CopiedSettingsFile)];

    public bool CopiedSettings => CopiedSettingsFiles.Count > 0;

    public bool WroteApplicationSettings =>
        RewrittenSettingsFiles.Contains(LegacyDataCatalog.SettingsFileName, StringComparer.OrdinalIgnoreCase);

    public bool CopiedUiPreferences => CopiedSettingsFile(LegacyDataCatalog.UiPreferencesFileName);

    public IReadOnlyList<string> RewrittenSettingsFiles =>
        [.. CopiedSettingsFiles.Union(MigratedSettingsFiles, StringComparer.OrdinalIgnoreCase)];

    public StatisticsExternalWrite ExternalWrite => new(CopiedFiles);

    private bool CopiedAny(IEnumerable<string> relativePaths)
    {
        foreach (var path in relativePaths)
        {
            if (Copied(path))
            {
                return true;
            }
        }

        return false;
    }

    private bool Copied(string fileName)
    {
        var expected = LegacyDataCatalog.NormalizeRelativePath(fileName);
        return CopiedFiles.Any(file =>
            string.Equals(LegacyDataCatalog.NormalizeRelativePath(file), expected, StringComparison.OrdinalIgnoreCase));
    }

    private bool CopiedSettingsFile(string fileName)
    {
        return CopiedFiles.Any(file => string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private bool CopiedInDirectory(string directoryName)
    {
        var prefix = LegacyDataCatalog.NormalizeRelativePath(directoryName) + "/";
        return CopiedFiles.Any(file =>
            LegacyDataCatalog.NormalizeRelativePath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
