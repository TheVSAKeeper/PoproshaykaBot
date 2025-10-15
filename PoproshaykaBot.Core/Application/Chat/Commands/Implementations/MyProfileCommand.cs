using PoproshaykaBot.Core.Application.Statistics;
using PoproshaykaBot.Core.Domain.Models.Chat;

namespace PoproshaykaBot.Core.Application.Chat.Commands.Implementations;

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
        var userStats = statistics.GetUserStatistics(context.UserId);

        if (userStats == null)
        {
            return OutgoingMessage.Reply("У тебя пока нет статистики", context.MessageId);
        }

        var messageCount = FormatNumber(userStats.MessageCount);
        var firstSeen = FormatDateTime(userStats.FirstSeen);
        var lastSeen = FormatDateTime(userStats.LastSeen);
        var text = $"👤 Твой профиль: {messageCount} сообщений | Впервые: {firstSeen} | Последний раз: {lastSeen}";
        return OutgoingMessage.Reply(text, context.MessageId);
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDateTime(DateTime dateTime)
    {
        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, moscowTimeZone);
        return moscowTime.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " по МСК";
    }
}
