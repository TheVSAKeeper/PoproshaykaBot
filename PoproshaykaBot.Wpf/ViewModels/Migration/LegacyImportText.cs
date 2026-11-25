using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using System.Globalization;
using System.IO;

namespace PoproshaykaBot.Wpf.ViewModels.Migration;

public static class LegacyImportText
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

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

        if (result.Failures.Count > 0)
        {
            return result.HasChanges
                ? "Данные перенесены не полностью."
                : "Перенести данные не удалось.";
        }

        if (result.UnmigratedLegacyFiles.Count > 0)
        {
            return "Часть данных осталась на прежнем месте.";
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

        if (result.Failures.Count > 0)
        {
            lines.Add($"Не удалось перенести: {Count(result.Failures.Count, "файл", "файла", "файлов")}.");
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
        var bot = result.BotAuthorizationRequired
            ? isSettingsEntry ? "После перезапуска войдите в Twitch с аккаунтом бота." : "Для работы бота нужно войти в Twitch."
            : null;
        var broadcaster = result.BroadcasterAuthorizationRecommended
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

        return $"{DescribeFile(failure.RelativeTargetPath)} – перенести не удалось, файл остался на прежнем месте.";
    }

    public static string Count(long value, string one, string few, string many)
    {
        return $"{value.ToString("N0", Russian)} {Plural(value, one, few, many)}";
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
