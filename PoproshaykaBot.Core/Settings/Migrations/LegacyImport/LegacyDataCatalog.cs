using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

internal enum LegacyDataCategory
{
    None = 0,
    UserStatistics = 1,
    BotStatistics = 2,
    StreamHistory = 3,
    PollHistory = 4,
    ChatRuntime = 5,
    Settings = 6,
    UiPreferences = 7,
    YouTubeQuota = 8,
    ChatHistory = 9,
}

internal sealed record LegacyDataEntry(
    string FileName,
    string RelativeTargetPath,
    LegacyDataCategory Category,
    bool AllowFlatFallback = false,
    bool CarriesTokens = false);

internal static class LegacyDataCatalog
{
    private static readonly LegacyDataEntry[] Entries =
    [
        new(SettingsFileNames.UserStatisticsCatalogFileName, SettingsFileNames.UserStatisticsCatalogFileName, LegacyDataCategory.UserStatistics),
        .. UserStatisticsPlatformCatalog.All.Select(static descriptor => new LegacyDataEntry(
            SettingsFileNames.UserStatisticsFileName,
            descriptor.RelativePath,
            LegacyDataCategory.UserStatistics)),
        new(SettingsFileNames.BotStatisticsFileName, SettingsFileNames.BotStatisticsFileName, LegacyDataCategory.BotStatistics),
        new(SettingsFileNames.CommandUsageFileName, SettingsFileNames.CommandUsageFileName, LegacyDataCategory.BotStatistics),
        new("quota.json", SettingsFileNames.YouTubeQuotaFileName, LegacyDataCategory.YouTubeQuota),
        new(SettingsFileNames.BroadcastsFileName, SettingsFileNames.BroadcastsFileName, LegacyDataCategory.StreamHistory),
        new(SettingsFileNames.ActiveBroadcastFileName, SettingsFileNames.ActiveBroadcastFileName, LegacyDataCategory.StreamHistory),
        new(SettingsFileNames.StreamSessionsFileName, SettingsFileNames.StreamSessionsFileName, LegacyDataCategory.StreamHistory),
        new(SettingsFileNames.PollsHistoryFileName, SettingsFileNames.PollsHistoryFileName, LegacyDataCategory.PollHistory),
        new(SettingsFileNames.UnknownCommandsFileName, SettingsFileNames.UnknownCommandsFileName, LegacyDataCategory.ChatRuntime),
        new(SettingsFileNames.ChatBlockersFileName, SettingsFileNames.ChatBlockersFileName, LegacyDataCategory.ChatRuntime),
        new(SettingsFileNames.ChatZoomFileName, SettingsFileNames.ChatZoomFileName, LegacyDataCategory.ChatRuntime),
        new(SettingsFileNames.SettingsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.SettingsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true, CarriesTokens: true),
        new(SettingsFileNames.ChatSettingsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ChatSettingsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.AccountsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.AccountsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true, CarriesTokens: true),
        new(SettingsFileNames.BroadcastProfilesFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.BroadcastProfilesFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.BroadcastProfilePlatformsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.BroadcastProfilePlatformsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.BroadcastProfileYouTubeFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.BroadcastProfileYouTubeFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.CommandSettingsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.CommandSettingsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.DashboardLayoutFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.DashboardLayoutFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.ObsChatFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ObsChatFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.ObsIntegrationFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ObsIntegrationFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.PollsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.PollsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.PlatformLifecycleFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.PlatformLifecycleFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.RecentCategoriesFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.RecentCategoriesFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.UpdateFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.UpdateFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.UiPreferencesFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.UiPreferencesFileName}", LegacyDataCategory.UiPreferences, AllowFlatFallback: true),
        new(SettingsFileNames.YouTubeSettingsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.YouTubeSettingsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
        new(SettingsFileNames.YouTubeAccountsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.YouTubeAccountsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true, CarriesTokens: true),
        new(SettingsFileNames.ChannelsFileName, $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ChannelsFileName}", LegacyDataCategory.Settings, AllowFlatFallback: true),
    ];

    public static IReadOnlyList<LegacyDataEntry> AllEntries => Entries;

    public static readonly string[] SettingsFiles =
    [
        .. Entries
            .Where(static entry => entry.Category is LegacyDataCategory.Settings or LegacyDataCategory.UiPreferences)
            .Select(static entry => entry.FileName),
    ];

    public static readonly string[] StoreOwnedSettingsFiles =
    [
        .. Entries
            .Where(static entry => entry.Category == LegacyDataCategory.Settings)
            .Select(static entry => entry.FileName),
    ];

    public static IEnumerable<string> GetRelativePaths(LegacyDataCategory category)
    {
        foreach (var entry in Entries)
        {
            if (entry.Category == category)
            {
                yield return entry.RelativeTargetPath;
            }
        }
    }

    public static List<LegacyDataFile> Enumerate(string sourceDirectory)
    {
        var files = new List<LegacyDataFile>();

        foreach (var entry in Entries)
        {
            var sourcePath = entry.AllowFlatFallback
                ? ResolveSettingsFile(sourceDirectory, entry.FileName)
                : Path.Combine(sourceDirectory, ToNativePath(entry.RelativeTargetPath));

            if (sourcePath is not null
                && TryDescribe(entry.FileName, sourcePath, ToNativePath(entry.RelativeTargetPath)) is { } file)
            {
                files.Add(file);
            }
        }

        var chatHistoryDirectory = Path.Combine(sourceDirectory, SettingsFileNames.ChatHistoryFolderName);
        if (Directory.Exists(chatHistoryDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(chatHistoryDirectory).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                if (!ChatHistoryCacheStore.IsCacheFileName(name))
                {
                    continue;
                }

                if (TryDescribe(name, path, Path.Combine(SettingsFileNames.ChatHistoryFolderName, name)) is { } file)
                {
                    files.Add(file);
                }
            }
        }

        return files;
    }

    public static bool IsChatHistoryPath(string relativePath)
    {
        return NormalizeRelativePath(relativePath).StartsWith(SettingsFileNames.ChatHistoryFolderName + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    public static string? ResolveSettingsFile(string sourceDirectory, string fileName)
    {
        var nested = Path.Combine(sourceDirectory, SettingsFileNames.SettingsFolderName, fileName);

        if (File.Exists(nested))
        {
            return nested;
        }

        var allowsFlatFallback = Entries.Any(entry =>
            entry.AllowFlatFallback
            && string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (!allowsFlatFallback)
        {
            return null;
        }

        var flat = Path.Combine(sourceDirectory, fileName);

        return File.Exists(flat) ? flat : null;
    }

    public static bool CarriesTokens(string fileName)
    {
        return Entries.Any(entry =>
            entry.CarriesTokens
            && string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasFlatSettingsFiles(string sourceDirectory)
    {
        return Entries
            .Where(static entry => entry.AllowFlatFallback)
            .Any(entry => File.Exists(Path.Combine(sourceDirectory, entry.FileName)));
    }

    private static string ToNativePath(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static LegacyDataFile? TryDescribe(string name, string path, string relativeTargetPath)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return null;
            }

            return new(name, path, relativeTargetPath, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
