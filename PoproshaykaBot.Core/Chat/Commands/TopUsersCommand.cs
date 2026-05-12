using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class TopUsersCommand(IUserStatisticsRepository statistics, UserRankService rankService) : IChatCommand
{
    private static readonly HashSet<string> MessagesKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "сообщения", "сообщ", "сообщ.", "msg", "messages", "m",
    };

    private static readonly HashSet<string> PointsKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "баллы", "балл", "points", "p", "b",
    };

    public string Canonical => "топпользователи";
    public IReadOnlyCollection<string> Aliases => ["top"];
    public string Description => "топ по баллам (аргумент 'сообщения' переключает на топ по сообщениям, число задаёт длину 1–20)";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var count = 5;
        var mode = UserTopMode.Points;

        foreach (var arg in context.Arguments)
        {
            if (int.TryParse(arg, out var requestedCount))
            {
                count = Math.Clamp(requestedCount, 1, 20);
            }
            else if (MessagesKeywords.Contains(arg))
            {
                mode = UserTopMode.Messages;
            }
            else if (PointsKeywords.Contains(arg))
            {
                mode = UserTopMode.Points;
            }
        }

        var topUsers = statistics.GetTop(count, mode);

        if (topUsers.Count == 0)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal("Пока нет данных о пользователях"));
        }

        var parts = topUsers
            .Select((x, i) =>
            {
                var rank = rankService.GetRank(x.Points);
                var value = mode == UserTopMode.Messages
                    ? $"{FormattingUtils.FormatNumber((long)x.MessageCount)} сообщ."
                    : rankService.FormatPoints(x.Points);

                return $"{i + 1}. {rank.Emoji} {x.Name} ({value})";
            })
            .ToList();

        var header = mode == UserTopMode.Messages
            ? $"💬 Топ-{topUsers.Count} по сообщениям: "
            : $"🏆 Топ-{topUsers.Count} по баллам: ";

        var text = header + string.Join(", ", parts);
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(text));
    }
}
