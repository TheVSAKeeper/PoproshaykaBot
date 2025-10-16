using PoproshaykaBot.Core.Application.Statistics;
using PoproshaykaBot.Core.Domain.Models.Chat;

namespace PoproshaykaBot.Core.Application.Chat.Commands.Implementations;

public sealed class TopUsersCommand(StatisticsCollector statistics) : IChatCommand
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
        var topUsers = statistics.GetTopUsers(5);

        if (topUsers.Count == 0)
        {
            return OutgoingMessage.Normal("Пока нет данных о пользователях");
        }

        var parts = topUsers
            .Select((x, i) => $"{i + 1}. {x.Name} ({FormatNumber(x.MessageCount)})")
            .ToList();

        var text = "🏆 Топ-5 активных пользователей: " + string.Join(" | ", parts);
        return OutgoingMessage.Normal(text);
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
