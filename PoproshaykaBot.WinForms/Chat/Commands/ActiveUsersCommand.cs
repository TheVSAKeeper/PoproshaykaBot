namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class ActiveUsersCommand(AudienceTracker audienceTracker) : IChatCommand
{
    public string Canonical => "пользователи";
    public IReadOnlyCollection<string> Aliases => ["users", "чат"];
    public string Description => "активные пользователи чата";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var text = audienceTracker.BuildActiveUsersSummary();
        return OutgoingMessage.Normal(text);
    }
}
