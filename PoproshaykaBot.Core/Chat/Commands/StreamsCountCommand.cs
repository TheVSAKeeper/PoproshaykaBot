using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class StreamsCountCommand(BroadcastHistoryStore historyStore, TimeProvider timeProvider) : IChatCommand
{
    public string Canonical => "стримов";
    public IReadOnlyCollection<string> Aliases => ["streams", "streamscount"];
    public string Description => "всего стримов и суммарная длительность";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var broadcasts = historyStore.LoadVisible().Broadcasts;

        if (broadcasts.Count == 0)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal("Истории стримов пока нет"));
        }

        var monthAgo = timeProvider.GetUtcNow() - TimeSpan.FromDays(30);
        var totalDuration = TimeSpan.Zero;
        var monthCount = 0;

        foreach (var broadcast in broadcasts)
        {
            totalDuration += broadcast.Duration;

            if (broadcast.StartedAt >= monthAgo)
            {
                monthCount++;
            }
        }

        var total = FormattingUtils.FormatNumber(broadcasts.Count);
        var totalDurationText = FormattingUtils.FormatTimeSpan(totalDuration);
        var month = FormattingUtils.FormatNumber(monthCount);

        var text = $"📺 Всего стримов: {total} | В эфире суммарно: {totalDurationText} | За 30 дней: {month}";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(text));
    }
}
