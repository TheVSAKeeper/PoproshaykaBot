using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class BotStatsCommand(IBotStatisticsRepository statistics) : IChatCommand
{
    public string Canonical => "статистикабота";
    public IReadOnlyCollection<string> Aliases => ["stats"];
    public string Description => "общая статистика бота";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var botStats = statistics.GetSnapshot();
        var uptime = FormattingUtils.FormatTimeSpan(botStats.TotalUptime);
        var totalMessages = FormattingUtils.FormatNumber(botStats.TotalMessagesProcessed);
        var startTime = FormattingUtils.FormatDateTime(botStats.BotStartTime);
        var text = $"📊 Бот: {totalMessages} сообщений | Аптайм: {uptime} | Старт: {startTime}";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(text));
    }
}
