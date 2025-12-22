using System.Globalization;

namespace PoproshaykaBot.WinForms.Chat.Commands;

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
        var text = $"📊 Бот: {totalMessages} сообщений | Аптайм: {uptime} | Старт: {startTime}";
        return OutgoingMessage.Normal(text);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{(int)timeSpan.TotalDays}д {timeSpan.Hours}ч {timeSpan.Minutes}м";
        }

        if (timeSpan.TotalHours >= 1)
        {
            return $"{timeSpan.Hours}ч {timeSpan.Minutes}м";
        }

        return $"{timeSpan.Minutes}м {timeSpan.Seconds}с";
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDateTime(DateTime dateTime)
    {
        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, moscowTimeZone);
        return moscowTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU")) + " МСК";
    }
}
