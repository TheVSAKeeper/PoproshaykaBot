using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

public sealed record FirstRunImportSnapshot(
    int? BroadcastProfilesCount,
    bool HasObsChat,
    int? HttpServerPort,
    bool HasObsIntegration,
    bool ObsEnabled,
    string? ObsHost,
    int ObsPort,
    string? ObsDashboardMicrophoneName,
    int ObsAudioSourcesCount,
    bool HasAutoBroadcast,
    bool AutoBroadcastEnabled,
    int AutoBroadcastIntervalMinutes,
    bool HasPlatformLifecycle,
    bool HasAppSettings,
    int RanksCount,
    int? CommandsCount,
    int ViewersCount,
    int SessionsCount,
    ulong TotalMessagesProcessed,
    bool HasAnyStatsFile,
    bool TwitchEnabled,
    bool TwitchBotAuthMissing);

public static class FirstRunSummaryReader
{
    private static readonly string[] ViewerStatsFiles =
    [
        SettingsFileNames.UserStatisticsCatalogFileName,
        SettingsFileNames.UserStatisticsFileName,
        $"{SettingsFileNames.YouTubeFolderName}/{SettingsFileNames.UserStatisticsFileName}",
    ];

    private static readonly string[] SessionStatsFiles =
    [
        SettingsFileNames.BroadcastsFileName,
        SettingsFileNames.StreamSessionsFileName,
    ];

    private static readonly string[] AllStatsFiles =
    [
        .. ViewerStatsFiles,
        .. SessionStatsFiles,
        SettingsFileNames.BotStatisticsFileName,
    ];

    private static readonly JsonSerializerOptions SummaryJsonOptions = new(JsonStoreOptions.Default)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static FirstRunImportSnapshot Read(LegacyImportResult result, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Read(result.TargetPath, result, logger);
    }

    public static FirstRunImportSnapshot Read(string targetBaseDirectory, LegacyImportResult result, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var effectiveTarget = string.IsNullOrWhiteSpace(targetBaseDirectory) ? result.TargetPath : targetBaseDirectory;
        var availableTargets = BuildAvailableTargetSet(result);

        var profiles = ReadJson<BroadcastProfilesSettings>(
            ResolveTargetFile(effectiveTarget, availableTargets,
                $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.BroadcastProfilesFileName}",
                SettingsFileNames.BroadcastProfilesFileName),
            logger);

        var obsChatPath = ResolveTargetFile(effectiveTarget, availableTargets,
            $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ObsChatFileName}",
            SettingsFileNames.ObsChatFileName);
        var hasObsChat = obsChatPath is not null && LegacyDataReader.TryRead(obsChatPath, logger) is JsonObject;

        var appSettings = ReadJson<AppSettings>(
            ResolveTargetFile(effectiveTarget, availableTargets,
                $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.SettingsFileName}",
                SettingsFileNames.SettingsFileName),
            logger);

        var obs = ReadJson<ObsIntegrationSettings>(
            ResolveTargetFile(effectiveTarget, availableTargets,
                $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.ObsIntegrationFileName}",
                SettingsFileNames.ObsIntegrationFileName),
            logger);

        var hasLifecycle = ResolveTargetFile(effectiveTarget, availableTargets,
            $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.PlatformLifecycleFileName}",
            SettingsFileNames.PlatformLifecycleFileName) is not null;

        var commands = ReadJson<CommandSettings>(
            ResolveTargetFile(effectiveTarget, availableTargets,
                $"{SettingsFileNames.SettingsFolderName}/{SettingsFileNames.CommandSettingsFileName}",
                SettingsFileNames.CommandSettingsFileName),
            logger);

        var viewersCount = CountViewers(effectiveTarget, availableTargets, logger);
        var sessionsCount = CountSessions(effectiveTarget, availableTargets, logger);
        var botNode = LegacyDataReader.TryRead(
            ResolveTargetFile(effectiveTarget, availableTargets, SettingsFileNames.BotStatisticsFileName),
            logger);
        var totalMessagesProcessed = LegacyDataReader.ReadUInt64(botNode, "totalMessagesProcessed");

        var hasAnyStatsFile = ResolveTargetFile(effectiveTarget, availableTargets, AllStatsFiles) is not null;

        var targetAppSettings = appSettings ?? ReadSettingsFromTarget(effectiveTarget, logger);
        var twitchEnabled = targetAppSettings?.Twitch.Enabled ?? true;
        var twitchBotAuthMissing = string.IsNullOrWhiteSpace(effectiveTarget)
            ? result.BotAuthorizationRequired
            : !LegacyDataReader.ReadTwitchAuthorization(effectiveTarget, logger).BotReady;

        return new(
            BroadcastProfilesCount: profiles?.Profiles.Count,
            HasObsChat: hasObsChat,
            HttpServerPort: appSettings?.Twitch.HttpServerPort is > 0 ? appSettings.Twitch.HttpServerPort : null,
            HasObsIntegration: obs is not null,
            ObsEnabled: obs?.Enabled ?? false,
            ObsHost: obs?.Host,
            ObsPort: obs?.Port ?? 0,
            ObsDashboardMicrophoneName: string.IsNullOrWhiteSpace(obs?.DashboardMicrophoneName)
                ? null
                : obs.DashboardMicrophoneName.Trim(),
            ObsAudioSourcesCount: obs?.GetDashboardSourceNames().Count ?? 0,
            HasAutoBroadcast: appSettings is not null,
            AutoBroadcastEnabled: appSettings?.Twitch.AutoBroadcast.AutoBroadcastEnabled ?? false,
            AutoBroadcastIntervalMinutes: appSettings?.Twitch.AutoBroadcast.BroadcastIntervalMinutes ?? 0,
            HasPlatformLifecycle: hasLifecycle,
            HasAppSettings: appSettings is not null,
            RanksCount: appSettings?.Ranks.Ranks.Count ?? 0,
            CommandsCount: commands?.Commands.Count,
            ViewersCount: viewersCount,
            SessionsCount: sessionsCount,
            TotalMessagesProcessed: totalMessagesProcessed,
            HasAnyStatsFile: hasAnyStatsFile,
            TwitchEnabled: twitchEnabled,
            TwitchBotAuthMissing: twitchBotAuthMissing);
    }

    private static HashSet<string> BuildAvailableTargetSet(LegacyImportResult result)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in result.CopiedFiles)
        {
            set.Add(LegacyDataCatalog.NormalizeRelativePath(file));
        }

        foreach (var migrated in result.MigratedSettingsFiles)
        {
            var normalized = LegacyDataCatalog.NormalizeRelativePath(migrated);
            set.Add(normalized);
            if (!normalized.StartsWith(SettingsFileNames.SettingsFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                set.Add($"{SettingsFileNames.SettingsFolderName}/{normalized}");
            }
        }

        return set;
    }

    private static string? ResolveTargetFile(
        string targetPath,
        HashSet<string> availableTargets,
        params string[] relativeCandidates)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || availableTargets.Count == 0)
        {
            return null;
        }

        const string settingsPrefix = SettingsFileNames.SettingsFolderName + "/";

        foreach (var candidate in relativeCandidates)
        {
            var normalized = LegacyDataCatalog.NormalizeRelativePath(candidate);
            var isAvailable = availableTargets.Contains(normalized)
                || (normalized.StartsWith(settingsPrefix, StringComparison.OrdinalIgnoreCase)
                    && availableTargets.Contains(normalized[settingsPrefix.Length..]));
            if (!isAvailable)
            {
                continue;
            }

            var full = Path.Combine(targetPath, candidate.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private static int CountViewers(string targetPath, HashSet<string> availableTargets, ILogger? logger)
    {
        var catalog = LegacyDataReader.TryRead(
            ResolveTargetFile(targetPath, availableTargets, ViewerStatsFiles[0]),
            logger);
        if (LegacyDataReader.Property(catalog, "accounts") is { } accounts)
        {
            var count = LegacyDataReader.CountItems(accounts);
            if (count > 0)
            {
                return count;
            }
        }

        var total = 0;
        foreach (var candidate in ViewerStatsFiles.AsSpan(1))
        {
            var users = LegacyDataReader.TryRead(
                ResolveTargetFile(targetPath, availableTargets, candidate),
                logger);
            total += LegacyDataReader.CountItems(users);
        }

        return total;
    }

    private static int CountSessions(string targetPath, HashSet<string> availableTargets, ILogger? logger)
    {
        var broadcasts = LegacyDataReader.TryRead(
            ResolveTargetFile(targetPath, availableTargets, SessionStatsFiles[0]),
            logger);
        var broadcastCount = LegacyDataReader.CountItems(
            LegacyDataReader.Property(broadcasts, "broadcasts")
            ?? LegacyDataReader.Property(broadcasts, "records")
            ?? broadcasts);
        if (broadcastCount > 0)
        {
            return broadcastCount;
        }

        var sessions = LegacyDataReader.TryRead(
            ResolveTargetFile(targetPath, availableTargets, SessionStatsFiles[1]),
            logger);
        return LegacyDataReader.CountItems(LegacyDataReader.Property(sessions, "sessions") ?? sessions);
    }

    private static AppSettings? ReadSettingsFromTarget(string targetBaseDirectory, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(targetBaseDirectory))
        {
            return null;
        }

        var path = LegacyDataCatalog.ResolveSettingsFile(targetBaseDirectory, SettingsFileNames.SettingsFileName);
        return ReadJson<AppSettings>(path, logger);
    }

    private static T? ReadJson<T>(string? filePath, ILogger? logger) where T : class
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), SummaryJsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogDebug(exception, "Сводка первого запуска: не удалось разобрать {FilePath}", filePath);
            return null;
        }
    }
}
