using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class WasThereCommand(
    BroadcastHistoryStore historyStore,
    IUserStatisticsRepository? userStatistics = null) : IChatCommand
{
    public string Canonical => "ятамбыл";
    public IReadOnlyCollection<string> Aliases => ["iwasthere", "attended"];
    public string Description => "на скольких стримах ты был";

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

        if (string.IsNullOrWhiteSpace(context.UserId))
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Ты ещё ни на одном завершённом стриме не был", context.MessageId));
        }

        var key = new PlatformUserKey(context.Platform, context.UserId);
        var keys = userStatistics?.GetLinkedKeys(key) ?? new HashSet<PlatformUserKey> { key };
        var attended = broadcasts
            .Where(b => b.HasAnyChatter(keys))
            .OrderByDescending(b => b.StartedAt)
            .ToList();

        if (attended.Count == 0)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Ты ещё ни на одном завершённом стриме не был", context.MessageId));
        }

        var lastDate = FormattingUtils.FormatDateTime(attended[0].StartedAt.UtcDateTime);
        var text = $"🎟 Ты был на {attended.Count} стримах из {broadcasts.Count} | Последний: {lastDate}";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
