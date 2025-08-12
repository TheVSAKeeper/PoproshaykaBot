namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class HelpCommand(Func<IReadOnlyCollection<IChatCommand>> getAllCommands) : IChatCommand
{
    public string Canonical => "помощь";
    public IReadOnlyCollection<string> Aliases => ["help", "h"];
    public string Description => "список команд";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var commands = getAllCommands()
            .OrderBy(x => x.Canonical, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (commands.Count == 0)
        {
            return OutgoingMessage.Reply("Команды недоступны", context.MessageId);
        }

        var items = commands
            .Select(x =>
            {
                var aliases = x.Aliases
                    .Where(a => string.IsNullOrWhiteSpace(a) == false)
                    .Select(a => $"!{a}")
                    .ToList();

                var aliasesPart = aliases.Count > 0
                    ? $" (алиасы: {string.Join(", ", aliases)})"
                    : string.Empty;

                return $"!{x.Canonical}{aliasesPart} — {x.Description}";
            })
            .ToList();

        var text = "Доступные команды: " + string.Join(" | ", items);
        return OutgoingMessage.Reply(text, context.MessageId);
    }
}
