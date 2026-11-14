using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class LastStreamCommand(BroadcastHistoryStore historyStore) : IChatCommand
{
    public string Canonical => "последнийстрим";
    public IReadOnlyCollection<string> Aliases => ["laststream", "last"];
    public string Description => "информация о последнем завершённом стриме";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var broadcasts = historyStore.LoadVisible().Broadcasts;

        if (broadcasts.Count == 0)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Истории стримов пока нет", context.MessageId));
        }

        var last = broadcasts.OrderByDescending(b => b.StartedAt).First();

        var title = string.IsNullOrWhiteSpace(last.Title) ? "Без названия" : last.Title;
        var game = string.IsNullOrWhiteSpace(last.Game) ? "Без категории" : last.Game;
        var duration = FormattingUtils.FormatTimeSpan(last.Duration);
        var messages = FormattingUtils.FormatNumber(last.MessageCount);
        var when = FormattingUtils.FormatDateTime(last.StartedAt.UtcDateTime);

        var text = $"📺 Последний стрим: {title} | {game} | ⏱ {duration} | 💬 {messages} | 👥 пик {last.PeakViewers} | {when}";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
