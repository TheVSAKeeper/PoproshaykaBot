using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private static readonly JsonSerializerOptions ImportSummaryJsonOptions = new(JsonStoreOptions.Default)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Dictionary<string, string> FileTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["settings.json"] = "Основные настройки",
        ["channels.json"] = "Каналы и группы каналов",
        ["chat.json"] = "Настройки чата",
        ["accounts.json"] = "Данные входа в Twitch",
        ["youtube.json"] = "Настройки YouTube",
        ["youtube-accounts.json"] = "Данные входа в YouTube",
        ["broadcast-profiles.json"] = "Профили трансляции",
        ["broadcast-profile-platforms.json"] = "Площадки профилей рассылки",
        ["broadcast-profile-youtube.json"] = "Параметры YouTube профилей рассылки",
        ["commands.json"] = "Настройки команд чата",
        ["command-usage.json"] = "Статистика использования команд",
        ["dashboard-layout.json"] = "Раскладка обзора",
        ["obs-chat.json"] = "Оформление чата для OBS",
        ["obs-integration.json"] = "Подключение к OBS",
        ["polls.json"] = "Профили голосований",
        ["platform-lifecycle.json"] = "Автозапуск бота по площадкам",
        ["polls-history.json"] = "История голосований",
        ["recent-categories.json"] = "Недавние категории",
        ["update.json"] = "Настройки обновлений",
        ["ui-preferences.toml"] = "Вид приложения",
        ["user_statistics_catalog.json"] = "Каталог статистики зрителей",
        ["users_statistics.json"] = "Статистика зрителей",
        ["youtube/users_statistics.json"] = "Статистика зрителей YouTube",
        ["youtube/quota.json"] = "Расход квоты YouTube",
        ["bot_statistics.json"] = "Статистика бота",
        ["broadcasts.json"] = "История эфиров",
        ["active_broadcast.json"] = "Активный эфир",
        ["stream_sessions.json"] = "История стримов",
        ["unknown_commands.txt"] = "Список неизвестных команд",
        ["chat-blockers.txt"] = "Правила скрытия в чате",
        ["chat-zoom.txt"] = "Масштаб чата",
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

    public static string BuildHeadline(LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Failures.Any(failure => !failure.DestinationWritten))
        {
            return result.HasChanges
                ? "Данные перенесены не полностью."
                : "Перенести данные не удалось.";
        }

        if (result.UnmigratedLegacyFiles.Count > 0)
        {
            return "Часть данных осталась на прежнем месте.";
        }

        if (result.Failures.Count > 0)
        {
            return "Данные перенесены. Часть исходных файлов сохранила прежние имена.";
        }

        if (result.IsInPlaceMigration && result.HasChanges)
        {
            return "Данные остались на месте и подготовлены к работе.";
        }

        return result.CopiedFiles.Count > 0
            ? "Данные перенесены."
            : "Переносить было нечего: все файлы уже на месте.";
    }

    public static string? BuildRestartNotice(LegacyImportResult result, bool isSettingsEntry)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!isSettingsEntry || !result.HasChanges)
        {
            return null;
        }

        const string Restart = "Перенесённые данные вступят в силу после перезапуска приложения.";

        var frozen = new List<string>();
        var pluralName = false;

        if (result.RewrittenSettingsFiles.Count > 0)
        {
            frozen.Add("настройки");
            pluralName = true;
        }

        if (result.CopiedStatistics)
        {
            frozen.Add("статистика");
        }

        if (result.CopiedStreamHistory)
        {
            frozen.Add("история стримов");
        }

        if (result.CopiedPollHistory)
        {
            frozen.Add("история голосований");
        }

        if (result.CopiedYouTubeQuota)
        {
            frozen.Add("расход квоты YouTube");
        }

        if (result.CopiedChatHistory)
        {
            frozen.Add("кэш истории чата");
        }

        if (frozen.Count == 0)
        {
            return Restart;
        }

        var names = frozen.Count == 1
            ? frozen[0]
            : $"{string.Join(", ", frozen.Take(frozen.Count - 1))} и {frozen[^1]}";

        var verb = frozen.Count == 1 && !pluralName ? "не сохраняется" : "не сохраняются";

        return $"{Restart} До перезапуска {names} {verb} – чтобы не затереть перенесённое.";
    }

    public static IReadOnlyList<string> BuildCounts(LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>();

        if (result.CopiedFiles.Count > 0)
        {
            lines.Add($"Перенесено: {Count(result.CopiedFiles.Count, "файл", "файла", "файлов")}.");
        }

        if (result.IsInPlaceMigration && result.MigratedSettingsFiles.Count > 0)
        {
            lines.Add($"Подготовлено на месте: {Count(result.MigratedSettingsFiles.Count, "файл", "файла", "файлов")}.");
        }

        if (result.SkippedFiles.Count > 0)
        {
            lines.Add($"Оставлено без изменений: {Count(result.SkippedFiles.Count, "файл", "файла", "файлов")} – такие данные здесь уже есть.");
        }

        var failedCopies = result.Failures.Count(failure => !failure.DestinationWritten);
        if (failedCopies > 0)
        {
            lines.Add($"Не удалось перенести: {Count(failedCopies, "файл", "файла", "файлов")}.");
        }

        if (result.HasChanges && (result.Failures.Count > 0 || result.UnmigratedLegacyFiles.Count > 0))
        {
            var written = result.CopiedFiles.Concat(result.MigratedSettingsFiles)
                .Select(DescribeFile)
                .Select(Decapitalize)
                .Distinct(StringComparer.CurrentCulture);
            lines.Add($"Записано: {string.Join(", ", written)}.");
        }

        return lines;
    }

    public static IReadOnlyList<string> BuildFailures(LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return [.. result.Failures.Select(DescribeFailure)];
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

    public static string? BuildAuthorizationNotice(LegacyImportResult result, bool isSettingsEntry)
    {
        ArgumentNullException.ThrowIfNull(result);

        return BuildAuthorizationNotice(
            result,
            isSettingsEntry,
            twitchEnabled: true,
            twitchBotRequired: result.BotAuthorizationRequired,
            youtubeBotRequired: false);
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

        var hasSettings = File.Exists(Path.Combine(fullPath, "settings.json"))
            || Directory.Exists(Path.Combine(fullPath, "settings"));

        return hasSettings
            ? "В выбранной папке не найдены подходящие файлы настроек или статистики PoproshaykaBot."
            : "В выбранной папке не найдены данные PoproshaykaBot: отсутствуют настройки (settings.json или папка settings) и файлы статистики (users_statistics.json, bot_statistics.json).";
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

        var transferred = new List<FirstRunSummaryItem>();
        var attention = new List<FirstRunSummaryItem>();
        var copiedTargets = result.CopiedFiles
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedTargets = result.Failures
            .Where(static failure => !failure.DestinationWritten)
            .Select(static failure => NormalizeRelativePath(failure.RelativeTargetPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetPath = result.TargetPath;

        AddBroadcastProfilesSummary(targetPath, copiedTargets, failedTargets, transferred, attention);
        AddOverlayAndPortSummary(targetPath, copiedTargets, failedTargets, transferred, attention);
        AddObsSummary(targetPath, copiedTargets, failedTargets, transferred, attention);
        AddAutoBroadcastSummary(targetPath, copiedTargets, failedTargets, transferred, attention);
        AddPhrasesAndRanksSummary(targetPath, copiedTargets, failedTargets, transferred, attention);
        AddStatisticsSummary(targetPath, copiedTargets, failedTargets, transferred, attention);

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

        if (BuildAuthorizationNotice(result, isSettingsEntry: false, twitchEnabled, twitchBotRequired, youtubeBotRequired: false) is { } authNotice)
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
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, "settings/broadcast-profiles.json", "broadcast-profiles.json"))
        {
            attention.Add(new("Профили эфира", "Не удалось перенести файл профилей – создайте или восстановите их в разделе «Каналы и профили».", IsWarning: true));
            return;
        }

        var profiles = ReadJson<BroadcastProfilesSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/broadcast-profiles.json", "broadcast-profiles.json"));
        if (profiles is { Profiles.Count: > 0 })
        {
            transferred.Add(new("Профили эфира", Count(profiles.Profiles.Count, "профиль", "профиля", "профилей")));
            return;
        }

        if (profiles is not null)
        {
            transferred.Add(new("Профили эфира", "Список профилей пуст – создать можно в разделе «Каналы и профили»"));
            return;
        }

        attention.Add(new("Профили эфира", "В старой папке не найдены – при необходимости создайте профили в разделе «Каналы и профили»."));
    }

    private static void AddOverlayAndPortSummary(
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        var obsChatFailed = IsAnyFailed(failedTargets, "settings/obs-chat.json", "obs-chat.json");
        var settingsFailed = IsAnyFailed(failedTargets, "settings/settings.json", "settings.json");

        var obsChatPath = obsChatFailed
            ? null
            : ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/obs-chat.json", "obs-chat.json");
        var hasObsChat = obsChatPath is not null && ReadJsonNode(obsChatPath) is JsonObject;
        var appSettings = settingsFailed
            ? null
            : ReadJson<AppSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/settings.json", "settings.json"));
        var port = appSettings?.Twitch.HttpServerPort;
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
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, "settings/obs-integration.json", "obs-integration.json"))
        {
            attention.Add(new("OBS и аудиоисточники", "Не удалось перенести настройки OBS – задайте подключение заново в разделе «Настройки».", IsWarning: true));
            return;
        }

        var obs = ReadJson<ObsIntegrationSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/obs-integration.json", "obs-integration.json"));
        if (obs is null)
        {
            attention.Add(new("OBS и аудиоисточники", "Настройки OBS в старой папке не найдены – при необходимости подключите OBS в Настройках."));
            return;
        }

        var parts = new List<string>
        {
            obs.Enabled ? $"подключение {obs.Host}:{obs.Port}" : "параметры подключения сохранены",
        };

        if (!string.IsNullOrWhiteSpace(obs.DashboardMicrophoneName))
        {
            parts.Add($"микрофон «{obs.DashboardMicrophoneName.Trim()}»");
        }

        var audioCount = obs.GetDashboardSourceNames().Count;
        if (audioCount > 0)
        {
            parts.Add(Count(audioCount, "аудиоисточник", "аудиоисточника", "аудиоисточников"));
        }

        transferred.Add(new("OBS и аудиоисточники", Capitalize(string.Join(", ", parts))));
    }

    private static void AddAutoBroadcastSummary(
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, "settings/settings.json", "settings.json", "settings/platform-lifecycle.json", "platform-lifecycle.json"))
        {
            attention.Add(new("Авто-рассылка", "Не удалось перенести настройки авто-рассылки – проверьте раздел «Автоматика».", IsWarning: true));
            return;
        }

        var appSettings = ReadJson<AppSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/settings.json", "settings.json"));
        var hasLifecycle = ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/platform-lifecycle.json", "platform-lifecycle.json") is not null;

        if (appSettings is null && !hasLifecycle)
        {
            attention.Add(new("Авто-рассылка", "Параметры авто-рассылки в старой папке не найдены."));
            return;
        }

        var auto = appSettings?.Twitch.AutoBroadcast;
        if (auto is not null)
        {
            var status = auto.AutoBroadcastEnabled
                ? $"включена, каждые {auto.BroadcastIntervalMinutes} мин"
                : $"перенесена (сейчас выключена, интервал {auto.BroadcastIntervalMinutes} мин)";
            transferred.Add(new("Авто-рассылка", Capitalize(status)));
            return;
        }

        transferred.Add(new("Авто-рассылка", "Настройки автозапуска и рассылки"));
    }

    private static void AddPhrasesAndRanksSummary(
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, "settings/settings.json", "settings.json", "settings/commands.json", "commands.json"))
        {
            attention.Add(new("Фразы и ранги", "Не удалось перенести часть фраз или рангов – проверьте раздел «Настройки» и «Бот».", IsWarning: true));
            return;
        }

        var appSettings = ReadJson<AppSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/settings.json", "settings.json"));
        var commands = ReadJson<CommandSettings>(ResolveCopiedTargetFile(targetPath, copiedTargets, "settings/commands.json", "commands.json"));

        if (appSettings is null && commands is null)
        {
            attention.Add(new("Фразы и ранги", "Пользовательские фразы и ранги в старой папке не найдены."));
            return;
        }

        var parts = new List<string>();
        if (appSettings is { Ranks.Ranks.Count: > 0 })
        {
            parts.Add(Count(appSettings.Ranks.Ranks.Count, "ранг", "ранга", "рангов"));
        }

        if (appSettings is not null)
        {
            parts.Add("фразы сообщений бота");
        }

        if (commands is { Commands.Count: > 0 })
        {
            parts.Add(Count(commands.Commands.Count, "настроенная команда", "настроенные команды", "настроенных команд"));
        }

        transferred.Add(new("Фразы и ранги", parts.Count > 0 ? Capitalize(string.Join(", ", parts)) : "Параметры рангов и фраз"));
    }

    private static void AddStatisticsSummary(
        string targetPath,
        HashSet<string> copiedTargets,
        HashSet<string> failedTargets,
        List<FirstRunSummaryItem> transferred,
        List<FirstRunSummaryItem> attention)
    {
        if (IsAnyFailed(failedTargets, "users_statistics.json", "user_statistics_catalog.json", "stream_sessions.json", "broadcasts.json", "bot_statistics.json"))
        {
            attention.Add(new("Статистика зрителей и сессий", "Часть файлов статистики не перенеслась и осталась в старой папке.", IsWarning: true));
            return;
        }

        var viewersCount = CountViewers(targetPath, copiedTargets);
        var sessionsCount = CountSessions(targetPath, copiedTargets);
        var botNode = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "bot_statistics.json"));
        var messagesProcessed = ReadUInt64Property(botNode, "totalMessagesProcessed");

        var parts = new List<string>();
        if (viewersCount > 0)
        {
            parts.Add(Count(viewersCount, "зритель", "зрителя", "зрителей"));
        }

        if (sessionsCount > 0)
        {
            parts.Add(Count(sessionsCount, "эфир", "эфира", "эфиров"));
        }

        if (parts.Count == 0 && messagesProcessed > 0)
        {
            parts.Add(Count((long)messagesProcessed, "сообщение", "сообщения", "сообщений"));
        }

        if (parts.Count > 0)
        {
            transferred.Add(new("Статистика зрителей и сессий", Capitalize(string.Join(", ", parts))));
            return;
        }

        var hasAnyStatsFile = ResolveCopiedTargetFile(targetPath, copiedTargets, "users_statistics.json", "user_statistics_catalog.json", "stream_sessions.json", "broadcasts.json", "bot_statistics.json") is not null;
        if (hasAnyStatsFile)
        {
            transferred.Add(new("Статистика зрителей и сессий", "Файлы статистики перенесены"));
        }
        else
        {
            attention.Add(new("Статистика зрителей и сессий", "Накопленная статистика зрителей и эфиров в старой папке не найдена."));
        }
    }

    private static int CountViewers(string targetPath, HashSet<string> copiedTargets)
    {
        var catalog = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "user_statistics_catalog.json"));
        if (GetJsonProperty(catalog, "accounts") is { } accounts)
        {
            var count = CountJsonItems(accounts);
            if (count > 0)
            {
                return count;
            }
        }

        var total = 0;
        var twitchUsers = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "users_statistics.json"));
        total += CountJsonItems(twitchUsers);

        var youtubeUsers = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "youtube/users_statistics.json"));
        total += CountJsonItems(youtubeUsers);

        return total;
    }

    private static int CountSessions(string targetPath, HashSet<string> copiedTargets)
    {
        var broadcasts = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "broadcasts.json"));
        var broadcastCount = CountJsonItems(GetJsonProperty(broadcasts, "records") ?? broadcasts);
        if (broadcastCount > 0)
        {
            return broadcastCount;
        }

        var sessions = ReadJsonNode(ResolveCopiedTargetFile(targetPath, copiedTargets, "stream_sessions.json"));
        return CountJsonItems(GetJsonProperty(sessions, "sessions") ?? sessions);
    }

    private static JsonNode? GetJsonProperty(JsonNode? node, string propertyName)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static int CountJsonItems(JsonNode? node)
    {
        return node switch
        {
            JsonArray array => array.Count,
            JsonObject obj => obj.Count,
            _ => 0,
        };
    }

    private static ulong ReadUInt64Property(JsonNode? node, string propertyName)
    {
        if (GetJsonProperty(node, propertyName) is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue(out ulong parsed))
        {
            return parsed;
        }

        if (value.TryGetValue(out long signed) && signed > 0)
        {
            return (ulong)signed;
        }

        return 0;
    }

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

    private static string? ResolveCopiedTargetFile(
        string targetPath,
        HashSet<string> copiedTargets,
        params string[] relativeCandidates)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || copiedTargets.Count == 0)
        {
            return null;
        }

        const string settingsPrefix = "settings/";
        foreach (var candidate in relativeCandidates)
        {
            var normalized = NormalizeRelativePath(candidate);
            var wasCopied = copiedTargets.Contains(normalized)
                || (normalized.StartsWith(settingsPrefix, StringComparison.OrdinalIgnoreCase)
                    && copiedTargets.Contains(normalized[settingsPrefix.Length..]));
            if (!wasCopied)
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

    private static JsonNode? ReadJsonNode(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(filePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static T? ReadJson<T>(string? filePath) where T : class
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), ImportSummaryJsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
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
