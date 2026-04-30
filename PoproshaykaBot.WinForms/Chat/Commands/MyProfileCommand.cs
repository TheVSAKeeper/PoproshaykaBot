using PoproshaykaBot.WinForms.Statistics;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class MyProfileCommand(StatisticsCollector statistics, UserRankService rankService) : IChatCommand
{
    public string Canonical => "мойпрофиль";
    public IReadOnlyCollection<string> Aliases => ["profile"];
    public string Description => "твоя статистика";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var targetUserId = context.UserId;
        var targetDisplayName = "твоя";

        if (context.Arguments.Count > 0)
        {
            var username = context.Arguments[0];
            var otherUserStats = statistics.GetUserStatisticsByName(username);
            if (otherUserStats != null)
            {
                targetUserId = otherUserStats.UserId;
                targetDisplayName = $"статистика {otherUserStats.Name}";
            }
            else
            {
                return OutgoingMessage.Reply($"Пользователь {username} не найден", context.MessageId);
            }
        }

        var userStats = statistics.GetUserStatistics(targetUserId);

        if (userStats == null)
        {
            var msg = targetUserId == context.UserId ? "У тебя пока нет статистики" : "У этого пользователя пока нет статистики";
            return OutgoingMessage.Reply(msg, context.MessageId);
        }

        var messageCount = FormattingUtils.FormatNumber(userStats.TotalMessageCount);
        var firstSeen = FormattingUtils.FormatDateTime(userStats.FirstSeen);
        var lastSeen = FormattingUtils.FormatDateTime(userStats.LastSeen);
        var rankDisplay = rankService.GetRankDisplay(userStats.TotalMessageCount);

        var text = $"👤 {targetDisplayName} {rankDisplay} | {messageCount} мсг | С нами с: {firstSeen} | В чате: {lastSeen}";
        return OutgoingMessage.Reply(text, context.MessageId);
    }
}
