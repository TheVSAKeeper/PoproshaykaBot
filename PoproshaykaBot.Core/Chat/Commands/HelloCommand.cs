namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class HelloCommand : IChatCommand
{
    public string Canonical => "привет";
    public IReadOnlyCollection<string> Aliases => [];
    public string Description => "поздороваться";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var text = $"Привет, {context.Username}!";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
