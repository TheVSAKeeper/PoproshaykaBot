namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class ActiveUsersCommand(AudienceTracker audienceTracker) : IChatCommand
{
    public string Canonical => "пользователи";
    public IReadOnlyCollection<string> Aliases => ["users", "чат"];
    public string Description => "активные пользователи чата";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var text = audienceTracker.BuildActiveUsersSummary();
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(text));
    }
}
