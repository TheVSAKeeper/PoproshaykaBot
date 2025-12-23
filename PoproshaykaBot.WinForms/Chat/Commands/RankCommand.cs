using System.Globalization;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class RankCommand(StatisticsCollector statistics, UserRankService rankService) : IChatCommand
{
    public string Canonical => "ранг";
    public IReadOnlyCollection<string> Aliases => ["rank"];
    public string Description => "посмотреть ранг";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var targetUserId = context.UserId;
        var targetDisplayName = "Твой";

        if (context.Arguments.Count > 0)
        {
            var username = context.Arguments[0];
            var otherUserStats = statistics.GetUserStatisticsByName(username);
            if (otherUserStats != null)
            {
                targetUserId = otherUserStats.UserId;
                targetDisplayName = $"Ранг {otherUserStats.Name}";
            }
            else
            {
                return OutgoingMessage.Reply($"Пользователь {username} не найден", context.MessageId);
            }
        }

        var userStats = statistics.GetUserStatistics(targetUserId);
        var messageCount = userStats?.TotalMessageCount ?? 0;
        var rankDisplay = rankService.GetRankDisplay(messageCount);

        var text = $"{targetDisplayName}: {rankDisplay} ({FormatNumber(messageCount)} сообщений)";
        return OutgoingMessage.Reply(text, context.MessageId);
    }

    private static string FormatNumber(long number)
    {
        return number.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
