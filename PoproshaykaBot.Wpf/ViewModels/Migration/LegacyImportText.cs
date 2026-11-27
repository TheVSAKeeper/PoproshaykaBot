using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using System.Globalization;
using System.IO;

namespace PoproshaykaBot.Wpf.ViewModels.Migration;

public sealed record FirstRunSummaryItem(string Title, string Detail, bool IsWarning = false);

public sealed record FirstRunImportSummary(
    string Headline,
    IReadOnlyList<FirstRunSummaryItem> TransferredItems,
    IReadOnlyList<FirstRunSummaryItem> AttentionItems,
    bool IsPartial);

public static class LegacyImportText
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Dictionary<string, string> FileTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        [SettingsFileNames.SettingsFileName] = "Основные настройки",
        [SettingsFileNames.ChannelsFileName] = "Каналы и группы каналов",
        [SettingsFileNames.ChatSettingsFileName] = "Настройки чата",
        [SettingsFileNames.AccountsFileName] = "Данные входа в Twitch",
        [SettingsFileNames.YouTubeSettingsFileName] = "Настройки YouTube",
        [SettingsFileNames.YouTubeAccountsFileName] = "Данные входа в YouTube",
        [SettingsFileNames.BroadcastProfilesFileName] = "Профили трансляции",
        [SettingsFileNames.BroadcastProfilePlatformsFileName] = "Площадки профилей рассылки",
        [SettingsFileNames.BroadcastProfileYouTubeFileName] = "Параметры YouTube профилей рассылки",
        [SettingsFileNames.CommandSettingsFileName] = "Настройки команд чата",
        [SettingsFileNames.CommandUsageFileName] = "Статистика использования команд",
        [SettingsFileNames.DashboardLayoutFileName] = "Раскладка обзора",
        [SettingsFileNames.ObsChatFileName] = "Оформление чата для OBS",
        [SettingsFileNames.ObsIntegrationFileName] = "Подключение к OBS",
        [SettingsFileNames.PollsFileName] = "Профили голосований",
        [SettingsFileNames.PlatformLifecycleFileName] = "Автозапуск бота по площадкам",
        [SettingsFileNames.PollsHistoryFileName] = "История голосований",
        [SettingsFileNames.RecentCategoriesFileName] = "Недавние категории",
        [SettingsFileNames.UpdateFileName] = "Настройки обновлений",
        [SettingsFileNames.UiPreferencesFileName] = "Вид приложения",
        [SettingsFileNames.UserStatisticsCatalogFileName] = "Каталог статистики зрителей",
        [SettingsFileNames.UserStatisticsFileName] = "Статистика зрителей",
        [$"{SettingsFileNames.YouTubeFolderName}/{SettingsFileNames.UserStatisticsFileName}"] = "Статистика зрителей YouTube",
        [SettingsFileNames.YouTubeQuotaFileName] = "Расход квоты YouTube",
        [SettingsFileNames.BotStatisticsFileName] = "Статистика бота",
        [SettingsFileNames.BroadcastsFileName] = "История эфиров",
        [SettingsFileNames.ActiveBroadcastFileName] = "Активный эфир",
        [SettingsFileNames.StreamSessionsFileName] = "История стримов",
        [SettingsFileNames.UnknownCommandsFileName] = "Список неизвестных команд",
        [SettingsFileNames.ChatBlockersFileName] = "Правила скрытия в чате",
        [SettingsFileNames.ChatZoomFileName] = "Масштаб чата",
    };

    public static string DescribeKind(LegacyDataSourceKind kind)
    {
        return kind switch
        {
            LegacyDataSourceKind.CurrentBaseDirectory => "Данные в рабочей папке",
            LegacyDataSourceKind.DefaultAppDataDirectory => "Данные предыдущей установки",
            _ => "Выбранная папка",
        };
    }

    public static string DescribeSourceTitle(LegacyDataSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Kind != LegacyDataSourceKind.SelectedFolder)
        {
            return DescribeKind(summary.Kind);
        }

        var trimmedPath = summary.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmedPath);
        var channel = string.IsNullOrWhiteSpace(summary.Channel)
            ? null
            : $"@{summary.Channel.Trim().TrimStart('@')}";

        return (string.IsNullOrWhiteSpace(folderName), channel) switch
        {
            (false, not null) => $"{folderName} ({channel})",
            (false, null) => folderName,
            (true, not null) => $"Профиль {channel}",
            (true, null) => string.IsNullOrWhiteSpace(trimmedPath) ? "Выбранная папка" : trimmedPath,
        };
    }

    public static string DescribeContent(LegacyDataSummary summary)
    {
        var parts = new List<string>();

        if (summary.HasMonolithicSettings || summary.HasSettingsDirectory)
        {
            parts.Add("Настройки");
        }

        if (!string.IsNullOrWhiteSpace(summary.Channel))
        {
            parts.Add($"канал @{summary.Channel.Trim().TrimStart('@')}");
        }

        var statistics = DescribeStatistics(summary);

        if (statistics is not null)
        {
            parts.Add(statistics);
        }

        if (parts.Count == 0)
        {
            return $"Файлы бота: {Count(summary.Files.Count, "файл", "файла", "файлов")}";
        }

        return string.Join(", ", parts);
    }

    public static string DescribeModified(LegacyDataSummary summary)
    {
        if (summary.Files.Count == 0)
        {
            return "Дата последнего изменения неизвестна";
        }

        var modified = summary.Files.Max(file => file.ModifiedAtUtc).ToLocalTime();

        return $"Последнее изменение: {modified.ToString("d MMMM yyyy, HH:mm", Russian)}";
    }

    public static string? DescribeUnmigrated(LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.UnmigratedLegacyFiles.Count == 0)
        {
            return null;
        }

        var names = result.UnmigratedLegacyFiles
            .Select(DescribeFile)
            .Select(Decapitalize)
            .Distinct(StringComparer.CurrentCulture)
            .ToList();

        return $"Осталось на прежнем месте: {string.Join(", ", names)}. "
               + "В рабочей папке уже есть одноимённые файлы. Исходные и рабочие файлы сохранены.";
    }

    public static string? BuildAuthorizationNotice(
        LegacyImportResult result,
        bool isSettingsEntry,
        bool twitchEnabled,
        bool twitchBotRequired,
        bool youtubeBotRequired)
    {
        ArgumentNullException.ThrowIfNull(result);

        var missingBotPlatforms = new List<string>();
        if (twitchEnabled && twitchBotRequired)
        {
            missingBotPlatforms.Add("Twitch");
        }

        if (youtubeBotRequired)
        {
            missingBotPlatforms.Add("YouTube");
        }

        var bot = missingBotPlatforms.Count switch
        {
            0 => null,
            1 => isSettingsEntry
                ? $"После перезапуска войдите в {missingBotPlatforms[0]} с аккаунтом бота."
                : $"Для работы бота нужно войти в {missingBotPlatforms[0]}.",
            _ => isSettingsEntry
                ? $"После перезапуска войдите в {string.Join(" и ", missingBotPlatforms)} с аккаунтом бота."
                : $"Для работы бота нужно войти в {string.Join(" и ", missingBotPlatforms)}.",
        };

        var broadcaster = twitchEnabled && result.BroadcasterAuthorizationRecommended
            ? "Вход с аккаунтом стримера можно восстановить отдельно, если нужны опросы и управление эфиром."
            : null;

        return (bot, broadcaster) switch
        {
            (not null, not null) => $"{bot} {broadcaster}",
            _ => bot ?? broadcaster,
        };
    }

    public static string DescribeFailure(LegacyImportFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var file = DescribeFile(failure.RelativeTargetPath);
        return failure.DestinationWritten
            ? $"{file} – рабочий файл записан, исходный файл сохранён под прежним именем."
            : $"{file} – перенести не удалось, файл остался на прежнем месте.";
    }

    public static string Count(long value, string one, string few, string many)
    {
        return $"{value.ToString("N0", Russian)} {Plural(value, one, few, many)}";
    }

    public static string DiagnoseInvalidFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Путь к папке не указан. Выберите папку с данными предыдущей версии.";
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return "Указан некорректный путь к папке. Выберите существующую папку с данными бота.";
        }

        if (!Directory.Exists(fullPath))
        {
            return "Выбранная папка не найдена. Проверьте расположение и выберите другую папку.";
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                return "Выбранная папка пуста: в ней нет файлов настроек (settings.json или папки settings) и базы статистики бота.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "Не удалось прочитать содержимое папки. Проверьте права доступа или выберите другую папку.";
        }

        var hasSettings = File.Exists(Path.Combine(fullPath, SettingsFileNames.SettingsFileName))
            || Directory.Exists(Path.Combine(fullPath, SettingsFileNames.SettingsFolderName));

        return hasSettings
            ? "В выбранной папке не найдены подходящие файлы настроек или статистики PoproshaykaBot."
            : $"В выбранной папке не найдены данные PoproshaykaBot: отсутствуют настройки ({SettingsFileNames.SettingsFileName} или папка {SettingsFileNames.SettingsFolderName}) и файлы статистики ({SettingsFileNames.UserStatisticsFileName}, {SettingsFileNames.BotStatisticsFileName}).";
    }

    public static string BuildInvalidImportReason(LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Failures.Count == 0)
        {
            return "Не удалось перенести данные из выбранной папки. Выберите другую папку или начните с нуля.";
        }

        var failedItems = result.Failures
            .Select(static failure => DescribeFile(failure.RelativeTargetPath))
            .Select(Decapitalize)
            .Distinct(StringComparer.CurrentCulture)
            .Take(4)
            .ToArray();

        return failedItems.Length == 0
            ? "Не удалось прочитать файлы из выбранной папки. Выберите другую папку или начните с нуля."
            : $"Не удалось перенести данные ({string.Join(", ", failedItems)}). Выберите другую папку или начните с нуля.";
    }

    public static FirstRunImportSummary BuildFirstRunSummary(
        LegacyImportResult result,
        bool twitchEnabled = true,
        bool twitchBotRequired = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        var snapshot = FirstRunSummaryReader.Read(result) with
        {
            TwitchEnabled = twitchEnabled,
            TwitchBotAuthMissing = twitchBotRequired,
        };

        return BuildFirstRunSummary(result, snapshot);
    }

    public static FirstRunImportSummary BuildFirstRunSummary(
        LegacyImportResult result,
        FirstRunImportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(snapshot);

        var transferred = new List<FirstRunSummaryItem>();
        var attention = new List<FirstRunSummaryItem>();
        var failedTargets = result.Failures
            .Where(static failure => !failure.DestinationWritten)
            .Select(static failure => NormalizeRelativePath(failure.RelativeTargetPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AddBroadcastProfilesSummary(snapshot, failedTargets, transferred, attention);
        AddOverlayAndPortSummary(snapshot, failedTargets, transferred, attention);
        AddObsSummary(snapshot, failedTargets, transferred, attention);
        AddAutoBroadcastSummary(snapshot, failedTargets, transferred, attention);
        AddPhrasesAndRanksSummary(snapshot, failedTargets, transferred, attention);
        AddStatisticsSummary(snapshot, failedTargets, transferred, attention);

        foreach (var failure in result.Failures)
        {
            if (failure.DestinationWritten)
            {
                attention.Add(new(DescribeFile(failure.RelativeTargetPath), DescribeFailure(failure), IsWarning: true));
            }
        }

        if (DescribeUnmigrated(result) is { } unmigrated)
        {
            attention.Add(new("Файлы с прежними именами", unmigrated, IsWarning: true));
        }

        if (BuildAuthorizationNotice(
                result,
                isSettingsEntry: false,
                snapshot.TwitchEnabled,
                snapshot.TwitchBotAuthMissing,
                youtubeBotRequired: false) is { } authNotice)
        {
            attention.Add(new("Вход в Twitch", authNotice, IsWarning: true));
        }

        var hasPartialFailures = result.Failures.Count > 0 || result.UnmigratedLegacyFiles.Count > 0;
        var isPartial = hasPartialFailures || attention.Count > 0;
        var headline = result.Failures.Any(static f => !f.DestinationWritten)
            ? "Данные перенесены частично"
            : isPartial
                ? "Данные перенесены, есть заметки по настройке"
                : "Данные успешно перенесены";

        return new(headline, transferred, attention, isPartial);
    }

    private static void AddBroadcastProfilesSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, SettingsPath(SettingsFileNames.BroadcastProfilesFileName), SettingsFileNames.BroadcastProfilesFileName))
        {
            attention.Add(new("Профили эфира", "Не удалось перенести файл профилей – создайте или восстановите их в разделе «Каналы и профили».", IsWarning: true));
            return;
        }

        if (snapshot.BroadcastProfilesCount is { } count and > 0)
        {
            transferred.Add(new("Профили эфира", Count(count, "профиль", "профиля", "профилей")));
            return;
        }

        if (snapshot.BroadcastProfilesCount is not null)
        {
            transferred.Add(new("Профили эфира", "Список профилей пуст – создать можно в разделе «Каналы и профили»"));
            return;
        }

        attention.Add(new("Профили эфира", "В старой папке не найдены – при необходимости создайте профили в разделе «Каналы и профили»."));
    }

    private static void AddOverlayAndPortSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        var obsChatFailed = IsAnyFailed(failedTargets, SettingsPath(SettingsFileNames.ObsChatFileName), SettingsFileNames.ObsChatFileName);
        var settingsFailed = IsAnyFailed(failedTargets, SettingsPath(SettingsFileNames.SettingsFileName), SettingsFileNames.SettingsFileName);

        var hasObsChat = !obsChatFailed && snapshot.HasObsChat;
        var port = settingsFailed ? null : snapshot.HttpServerPort;
        var hasPort = port is > 0;

        if (hasObsChat && hasPort)
        {
            transferred.Add(new("Оверлей и локальный порт", $"Оформление чата OBS и порт {port}"));
        }
        else if (hasObsChat)
        {
            transferred.Add(new("Оверлей и локальный порт", "Оформление чата для OBS"));
        }
        else if (hasPort)
        {
            transferred.Add(new("Оверлей и локальный порт", $"Локальный порт веб-сервера {port}"));
        }

        if (obsChatFailed && settingsFailed)
        {
            attention.Add(new("Оверлей и локальный порт", "Не удалось перенести оформление чата OBS и локальный порт веб-сервера – проверьте раздел «Настройки».", IsWarning: true));
        }
        else if (obsChatFailed)
        {
            attention.Add(new("Оформление чата OBS", "Не удалось перенести оформление чата OBS – проверьте раздел «Настройки».", IsWarning: true));
        }
        else if (settingsFailed)
        {
            attention.Add(new("Локальный порт веб-сервера", "Не удалось перенести локальный порт веб-сервера – проверьте раздел «Настройки».", IsWarning: true));
        }
        else if (!hasObsChat && !hasPort)
        {
            attention.Add(new("Оверлей и локальный порт", "Отдельные настройки оверлея не найдены – используются значения по умолчанию."));
        }
    }

    private static void AddObsSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, SettingsPath(SettingsFileNames.ObsIntegrationFileName), SettingsFileNames.ObsIntegrationFileName))
        {
            attention.Add(new("OBS и аудиоисточники", "Не удалось перенести настройки OBS – задайте подключение заново в разделе «Настройки».", IsWarning: true));
            return;
        }

        if (!snapshot.HasObsIntegration)
        {
            attention.Add(new("OBS и аудиоисточники", "Настройки OBS в старой папке не найдены – при необходимости подключите OBS в Настройках."));
            return;
        }

        var parts = new List<string>
        {
            snapshot.ObsEnabled ? $"подключение {snapshot.ObsHost}:{snapshot.ObsPort}" : "параметры подключения сохранены",
        };

        if (!string.IsNullOrWhiteSpace(snapshot.ObsDashboardMicrophoneName))
        {
            parts.Add($"микрофон «{snapshot.ObsDashboardMicrophoneName.Trim()}»");
        }

        if (snapshot.ObsAudioSourcesCount > 0)
        {
            parts.Add(Count(snapshot.ObsAudioSourcesCount, "аудиоисточник", "аудиоисточника", "аудиоисточников"));
        }

        transferred.Add(new("OBS и аудиоисточники", Capitalize(string.Join(", ", parts))));
    }

    private static void AddAutoBroadcastSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(
                failedTargets,
                SettingsPath(SettingsFileNames.SettingsFileName),
                SettingsFileNames.SettingsFileName,
                SettingsPath(SettingsFileNames.PlatformLifecycleFileName),
                SettingsFileNames.PlatformLifecycleFileName))
        {
            attention.Add(new("Авто-рассылка", "Не удалось перенести настройки авто-рассылки – проверьте раздел «Автоматика».", IsWarning: true));
            return;
        }

        if (!snapshot.HasAutoBroadcast && !snapshot.HasPlatformLifecycle)
        {
            attention.Add(new("Авто-рассылка", "Параметры авто-рассылки в старой папке не найдены."));
            return;
        }

        if (snapshot.HasAutoBroadcast)
        {
            var status = snapshot.AutoBroadcastEnabled
                ? $"включена, каждые {snapshot.AutoBroadcastIntervalMinutes} мин"
                : $"перенесена (сейчас выключена, интервал {snapshot.AutoBroadcastIntervalMinutes} мин)";
            transferred.Add(new("Авто-рассылка", Capitalize(status)));
            return;
        }

        transferred.Add(new("Авто-рассылка", "Настройки автозапуска и рассылки"));
    }

    private static void AddPhrasesAndRanksSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(
                failedTargets,
                SettingsPath(SettingsFileNames.SettingsFileName),
                SettingsFileNames.SettingsFileName,
                SettingsPath(SettingsFileNames.CommandSettingsFileName),
                SettingsFileNames.CommandSettingsFileName))
        {
            attention.Add(new("Фразы и ранги", "Не удалось перенести часть фраз или рангов – проверьте раздел «Настройки» и «Бот».", IsWarning: true));
            return;
        }

        if (!snapshot.HasAppSettings && snapshot.CommandsCount is null)
        {
            attention.Add(new("Фразы и ранги", "Пользовательские фразы и ранги в старой папке не найдены."));
            return;
        }

        var parts = new List<string>();
        if (snapshot.RanksCount > 0)
        {
            parts.Add(Count(snapshot.RanksCount, "ранг", "ранга", "рангов"));
        }

        if (snapshot.HasAppSettings)
        {
            parts.Add("фразы сообщений бота");
        }

        if (snapshot.CommandsCount is { } commandsCount and > 0)
        {
            parts.Add(Count(commandsCount, "настроенная команда", "настроенные команды", "настроенных команд"));
        }

        transferred.Add(new("Фразы и ранги", parts.Count > 0 ? Capitalize(string.Join(", ", parts)) : "Параметры рангов и фраз"));
    }

    private static void AddStatisticsSummary(
        FirstRunImportSnapshot snapshot,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(
                failedTargets,
                SettingsFileNames.UserStatisticsFileName,
                SettingsFileNames.UserStatisticsCatalogFileName,
                SettingsFileNames.StreamSessionsFileName,
                SettingsFileNames.BroadcastsFileName,
                SettingsFileNames.BotStatisticsFileName))
        {
            attention.Add(new("Статистика зрителей и сессий", "Часть файлов статистики не перенеслась и осталась в старой папке.", IsWarning: true));
            return;
        }

        var parts = new List<string>();
        if (snapshot.ViewersCount > 0)
        {
            parts.Add(Count(snapshot.ViewersCount, "зритель", "зрителя", "зрителей"));
        }

        if (snapshot.SessionsCount > 0)
        {
            parts.Add(Count(snapshot.SessionsCount, "эфир", "эфира", "эфиров"));
        }

        if (parts.Count == 0 && snapshot.TotalMessagesProcessed > 0)
        {
            parts.Add(Count((long)snapshot.TotalMessagesProcessed, "сообщение", "сообщения", "сообщений"));
        }

        if (parts.Count > 0)
        {
            transferred.Add(new("Статистика зрителей и сессий", Capitalize(string.Join(", ", parts))));
            return;
        }

        if (snapshot.HasAnyStatsFile)
        {
            transferred.Add(new("Статистика зрителей и сессий", "Файлы статистики перенесены"));
        }
        else
        {
            attention.Add(new("Статистика зрителей и сессий", "Накопленная статистика зрителей и эфиров в старой папке не найдена."));
        }
    }

    private static string SettingsPath(string fileName) => $"{SettingsFileNames.SettingsFolderName}/{fileName}";

    private static bool IsAnyFailed(HashSet<string> failedTargets, params string[] relativePaths)
    {
        foreach (var path in relativePaths)
        {
            if (failedTargets.Contains(NormalizeRelativePath(path)))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];
    }

    private static string Decapitalize(string text)
    {
        return text.Length == 0 ? text : char.ToLower(text[0], Russian) + text[1..];
    }

    private static string DescribeFile(string relativeTargetPath)
    {
        if (string.IsNullOrWhiteSpace(relativeTargetPath))
        {
            return "Данные предыдущей версии";
        }

        var normalizedPath = relativeTargetPath.Replace('\\', '/');

        if (LegacyImportResult.IsChatHistoryPath(normalizedPath))
        {
            return "Кэш истории чата";
        }

        if (FileTitles.TryGetValue(normalizedPath, out var pathTitle))
        {
            return pathTitle;
        }

        var name = Path.GetFileName(relativeTargetPath);

        return FileTitles.TryGetValue(name, out var title) ? title : "Данные предыдущей версии";
    }

    private static string? DescribeStatistics(LegacyDataSummary summary)
    {
        var parts = new List<string>();

        if (summary.UserStatisticsCount > 0)
        {
            parts.Add(Count(summary.UserStatisticsCount, "пользователь", "пользователя", "пользователей"));
        }

        if (summary.TotalMessagesProcessed > 0)
        {
            parts.Add(Count((long)summary.TotalMessagesProcessed, "сообщение", "сообщения", "сообщений"));
        }

        return parts.Count == 0 ? null : $"статистика: {string.Join(", ", parts)}";
    }

    private static string Plural(long value, string one, string few, string many)
    {
        var absolute = Math.Abs(value);

        if (absolute % 100 is >= 11 and <= 14)
        {
            return many;
        }

        return (absolute % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }
}
