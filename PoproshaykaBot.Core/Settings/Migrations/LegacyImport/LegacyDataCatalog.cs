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
    public const string SettingsFolderName = "settings";
    public const string ChatHistoryFolderName = "chat-history";
    public const string YouTubeFolderName = "youtube";
    public const string SettingsFileName = "settings.json";
    public const string ChatSettingsFileName = "chat.json";
    public const string AccountsFileName = "accounts.json";
    public const string UserStatisticsFileName = "users_statistics.json";
    public const string BotStatisticsFileName = "bot_statistics.json";
    public const string BroadcastsFileName = "broadcasts.json";
    public const string ActiveBroadcastFileName = "active_broadcast.json";
    public const string StreamSessionsFileName = "stream_sessions.json";
    public const string BroadcastProfilePlatformsFileName = "broadcast-profile-platforms.json";
    public const string YouTubeQuotaFileName = "youtube/quota.json";
    public const string PollsHistoryFileName = "polls-history.json";
    public const string UiPreferencesFileName = "ui-preferences.toml";
    public const string YouTubeSettingsFileName = "youtube.json";
    public const string YouTubeAccountsFileName = "youtube-accounts.json";

    private static readonly LegacyDataEntry[] Entries =
    [
        .. UserStatisticsPlatformCatalog.All.Select(static descriptor => new LegacyDataEntry(
            UserStatisticsFileName,
            descriptor.RelativePath,
            LegacyDataCategory.UserStatistics)),
        new(BotStatisticsFileName, BotStatisticsFileName, LegacyDataCategory.BotStatistics),
        new("quota.json", YouTubeQuotaFileName, LegacyDataCategory.YouTubeQuota),
        new(BroadcastsFileName, BroadcastsFileName, LegacyDataCategory.StreamHistory),
        new(ActiveBroadcastFileName, ActiveBroadcastFileName, LegacyDataCategory.StreamHistory),
        new(StreamSessionsFileName, StreamSessionsFileName, LegacyDataCategory.StreamHistory),
        new(PollsHistoryFileName, PollsHistoryFileName, LegacyDataCategory.PollHistory),
        new("unknown_commands.txt", "unknown_commands.txt", LegacyDataCategory.ChatRuntime),
        new("chat-blockers.txt", "chat-blockers.txt", LegacyDataCategory.ChatRuntime),
        new("chat-zoom.txt", "chat-zoom.txt", LegacyDataCategory.ChatRuntime),
        new(SettingsFileName, $"{SettingsFolderName}/{SettingsFileName}", LegacyDataCategory.Settings, true, CarriesTokens: true),
        new(ChatSettingsFileName, $"{SettingsFolderName}/{ChatSettingsFileName}", LegacyDataCategory.Settings, true),
        new(AccountsFileName, $"{SettingsFolderName}/{AccountsFileName}", LegacyDataCategory.Settings, true, CarriesTokens: true),
        new("broadcast-profiles.json", $"{SettingsFolderName}/broadcast-profiles.json", LegacyDataCategory.Settings, true),
        new(BroadcastProfilePlatformsFileName, $"{SettingsFolderName}/{BroadcastProfilePlatformsFileName}", LegacyDataCategory.Settings, true),
        new("dashboard-layout.json", $"{SettingsFolderName}/dashboard-layout.json", LegacyDataCategory.Settings, true),
        new("obs-chat.json", $"{SettingsFolderName}/obs-chat.json", LegacyDataCategory.Settings, true),
        new("obs-integration.json", $"{SettingsFolderName}/obs-integration.json", LegacyDataCategory.Settings, true),
        new("polls.json", $"{SettingsFolderName}/polls.json", LegacyDataCategory.Settings, true),
        new("recent-categories.json", $"{SettingsFolderName}/recent-categories.json", LegacyDataCategory.Settings, true),
        new("update.json", $"{SettingsFolderName}/update.json", LegacyDataCategory.Settings, true),
        new(UiPreferencesFileName, $"{SettingsFolderName}/{UiPreferencesFileName}", LegacyDataCategory.UiPreferences, true),
        new(YouTubeSettingsFileName, $"{SettingsFolderName}/{YouTubeSettingsFileName}", LegacyDataCategory.Settings, true),
        new(YouTubeAccountsFileName, $"{SettingsFolderName}/{YouTubeAccountsFileName}", LegacyDataCategory.Settings, true, CarriesTokens: true),
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

        var chatHistoryDirectory = Path.Combine(sourceDirectory, ChatHistoryFolderName);
        if (Directory.Exists(chatHistoryDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(chatHistoryDirectory).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                if (!ChatHistoryCacheStore.IsCacheFileName(name))
                {
                    continue;
                }

                if (TryDescribe(name, path, Path.Combine(ChatHistoryFolderName, name)) is { } file)
                {
                    files.Add(file);
                }
            }
        }

        return files;
    }

    public static bool IsChatHistoryPath(string relativePath)
    {
        return NormalizeRelativePath(relativePath).StartsWith(ChatHistoryFolderName + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    public static string? ResolveSettingsFile(string sourceDirectory, string fileName)
    {
        var nested = Path.Combine(sourceDirectory, SettingsFolderName, fileName);

        if (File.Exists(nested))
        {
            return nested;
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
        return SettingsFiles.Any(name => File.Exists(Path.Combine(sourceDirectory, name)));
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
