namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

public sealed class LegacyDataSummary
{
    public required string SourcePath { get; init; }

    public required LegacyDataSourceKind Kind { get; init; }

    public required bool HasMonolithicSettings { get; init; }

    public required bool HasSettingsDirectory { get; init; }

    public string? Channel { get; init; }

    public string? BotLogin { get; init; }

    public int UserStatisticsCount { get; init; }

    public ulong TotalMessagesProcessed { get; init; }

    public required bool HasOAuthTokens { get; init; }

    public bool HasFlatTwitchTokens { get; init; }

    public IReadOnlyList<string> GeneratedTargetPaths { get; init; } = [];

    public required IReadOnlyList<LegacyDataFile> Files { get; init; }
}
