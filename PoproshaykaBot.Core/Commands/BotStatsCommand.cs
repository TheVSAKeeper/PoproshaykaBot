namespace PoproshaykaBot.Core.Commands;

public sealed class BotStatsCommand(StatisticsCollector statistics) : IChatCommand
{
    public string Canonical => "статистикабота";
    public IReadOnlyCollection<string> Aliases => ["stats"];
    public string Description => "общая статистика бота";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var botStats = statistics.GetBotStatistics();
        var uptime = FormatTimeSpan(botStats.TotalUptime);
        var totalMessages = FormatNumber(botStats.TotalMessagesProcessed);
        var startTime = FormatDateTime(botStats.BotStartTime);
        var text = $"📊 Статистика бота: Обработано {totalMessages} сообщений | Время работы: {uptime} | Запущен: {startTime}";
        return OutgoingMessage.Normal(text);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{(int)timeSpan.TotalDays} дн. {timeSpan.Hours} ч. {timeSpan.Minutes} мин.";
        }

        if (timeSpan.TotalHours >= 1)
        {
            return $"{timeSpan.Hours} ч. {timeSpan.Minutes} мин.";
        }

        return $"{timeSpan.Minutes} мин. {timeSpan.Seconds} сек.";
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDateTime(DateTime dateTime)
    {
        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, moscowTimeZone);
        return moscowTime.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " по МСК";
    }
}
