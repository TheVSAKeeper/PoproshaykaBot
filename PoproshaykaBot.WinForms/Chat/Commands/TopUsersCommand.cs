using PoproshaykaBot.WinForms.Statistics;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class TopUsersCommand(StatisticsCollector statistics, UserRankService rankService) : IChatCommand
{
    public string Canonical => "топпользователи";
    public IReadOnlyCollection<string> Aliases => ["top"];
    public string Description => "ТОП-5 по сообщениям";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var count = 5;

        if (context.Arguments.Count > 0 && int.TryParse(context.Arguments[0], out var requestedCount))
        {
            count = Math.Clamp(requestedCount, 1, 20);
        }

        var topUsers = statistics.GetTopUsers(count);

        if (topUsers.Count == 0)
        {
            return OutgoingMessage.Normal("Пока нет данных о пользователях");
        }

        var parts = topUsers
            .Select((x, i) =>
            {
                var rank = rankService.GetRank(x.TotalMessageCount);
                return $"{i + 1}. {rank.Emoji} {x.Name} ({FormattingUtils.FormatNumber(x.TotalMessageCount)})";
            })
            .ToList();

        var text = $"🏆 Топ-{topUsers.Count}: " + string.Join(", ", parts);
        return OutgoingMessage.Normal(text);
    }
}
