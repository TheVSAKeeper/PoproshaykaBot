using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class RankCommand(IUserStatisticsRepository statistics, UserRankService rankService) : IChatCommand
{
    public string Canonical => "ранг";
    public IReadOnlyCollection<string> Aliases => ["rank"];
    public string Description => "посмотреть ранг";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var targetUserId = context.UserId;
        var targetDisplayName = "Твой";

        if (context.Arguments.Count > 0)
        {
            var username = context.Arguments[0];
            var otherUserStats = statistics.GetByName(username);
            if (otherUserStats != null)
            {
                targetUserId = otherUserStats.UserId;
                targetDisplayName = $"Ранг {otherUserStats.Name}";
            }
            else
            {
                return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply($"Пользователь {username} не найден", context.MessageId));
            }
        }

        var userStats = statistics.GetById(targetUserId);
        var points = userStats?.Points ?? 0;
        var messages = (long?)userStats?.MessageCount ?? 0;
        var rankDisplay = rankService.GetRankDisplay(points);

        var text = $"{targetDisplayName}: {rankDisplay} ({rankService.FormatPoints(points)} / {FormattingUtils.FormatNumber(messages)} сообщ.)";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
