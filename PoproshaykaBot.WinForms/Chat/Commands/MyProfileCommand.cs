using System.Globalization;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class MyProfileCommand(StatisticsCollector statistics) : IChatCommand
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

        var messageCount = FormatNumber(userStats.MessageCount);
        var firstSeen = FormatDateTime(userStats.FirstSeen);
        var lastSeen = FormatDateTime(userStats.LastSeen);
        var text = $"👤 Профиль: {messageCount} мсг | {targetDisplayName} | С нами с: {firstSeen} | В чате: {lastSeen} МСК";
        return OutgoingMessage.Reply(text, context.MessageId);
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDateTime(DateTime dateTime)
    {
        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, moscowTimeZone);
        return moscowTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU")) + " МСК";
    }
}
