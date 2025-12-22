namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class HelloCommand : IChatCommand
{
    public string Canonical => "привет";
    public IReadOnlyCollection<string> Aliases => [];
    public string Description => "поздороваться";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var text = $"Привет, {context.Username}!";
        return OutgoingMessage.Reply(text, context.MessageId);
    }
}
