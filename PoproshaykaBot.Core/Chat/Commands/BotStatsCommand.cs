using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

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
        var uptime = FormattingUtils.FormatTimeSpan(botStats.TotalUptime);
        var totalMessages = FormattingUtils.FormatNumber(botStats.TotalMessagesProcessed);
        var startTime = FormattingUtils.FormatDateTime(botStats.BotStartTime);
        var text = $"📊 Бот: {totalMessages} сообщений | Аптайм: {uptime} | Старт: {startTime}";
        return OutgoingMessage.Normal(text);
    }
}
