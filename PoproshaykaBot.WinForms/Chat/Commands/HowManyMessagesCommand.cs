namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class HowManyMessagesCommand(StatisticsCollector statistics) : IChatCommand
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
        var userStats = statistics.GetUserStatistics(context.UserId);
        var messageCount = userStats?.MessageCount ?? 0;
        var text = $"У тебя {FormatNumber(messageCount)} сообщений";
        return OutgoingMessage.Reply(text, context.MessageId);
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
