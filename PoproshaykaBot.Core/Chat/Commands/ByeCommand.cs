namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class ByeCommand(AudienceTracker audienceTracker) : IChatCommand
{
    public string Canonical => "пока";
    public IReadOnlyCollection<string> Aliases => ["bye"];
    public string Description => "попрощаться";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var farewell = audienceTracker.CreateFarewell(context.UserId, context.DisplayName);

        if (string.IsNullOrWhiteSpace(farewell))
        {
            return Task.FromResult<OutgoingMessage?>(null);
        }

        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(farewell, context.MessageId));
    }
}
