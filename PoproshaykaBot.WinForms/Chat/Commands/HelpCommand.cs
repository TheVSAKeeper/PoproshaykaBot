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
        var allCommands = getAllCommands();

        if (context.Arguments.Count > 0)
        {
            var targetToken = context.Arguments[0].TrimStart('!');
            var command = allCommands.FirstOrDefault(x =>
                string.Equals(x.Canonical, targetToken, StringComparison.OrdinalIgnoreCase)
                || x.Aliases.Any(a => string.Equals(a, targetToken, StringComparison.OrdinalIgnoreCase)));

            if (command != null)
            {
                var aliases = command.Aliases.Count > 0
                    ? $" (алиасы: {string.Join(", ", command.Aliases.Select(x => "!" + x))})"
                    : string.Empty;

                var text = $"❓ !{command.Canonical}: {command.Description}{aliases}";
                return OutgoingMessage.Reply(text, context.MessageId);
            }
        }

        var commandNames = allCommands
            .OrderBy(x => x.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"!{x.Canonical}")
            .ToList();

        if (commandNames.Count == 0)
        {
            return OutgoingMessage.Reply("Команды недоступны", context.MessageId);
        }

        var responseText = "📋 Команды: " + string.Join(", ", commandNames);
        return OutgoingMessage.Reply(responseText, context.MessageId);
    }
}
