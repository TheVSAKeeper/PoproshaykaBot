using System.Globalization;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class HowManyMessagesCommand(StatisticsCollector statistics, UserRankService rankService) : IChatCommand
{
    public string Canonical => "сколькосообщений";
    public IReadOnlyCollection<string> Aliases => ["messages", "cc"];
    public string Description => "твой счётчик сообщений";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var targetUserId = context.UserId;
        var targetDisplayName = "У тебя";

        if (context.Arguments.Count > 0)
        {
            var username = context.Arguments[0];
            var otherUserStats = statistics.GetUserStatisticsByName(username);
            if (otherUserStats != null)
            {
                targetUserId = otherUserStats.UserId;
                targetDisplayName = $"У {otherUserStats.Name}";
            }
            else
            {
                return OutgoingMessage.Reply($"Пользователь {username} не найден", context.MessageId);
            }
        }

        var userStats = statistics.GetUserStatistics(targetUserId);
        var messageCount = userStats?.TotalMessageCount ?? 0;
        var rankDisplay = rankService.GetRankDisplay(messageCount);

        var text = $"{rankDisplay} | {targetDisplayName} {FormatNumber(messageCount)} сообщений";
        return OutgoingMessage.Reply(text, context.MessageId);
    }

    private static string FormatNumber(long number)
    {
        return number.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
