namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class ByeCommand(AudienceTracker audienceTracker) : IChatCommand
{
    public string Canonical => "пока";
    public IReadOnlyCollection<string> Aliases => ["bye"];
    public string Description => "попрощаться";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage? Execute(CommandContext context)
    {
        var farewell = audienceTracker.CreateFarewell(context.UserId, context.DisplayName);

        if (string.IsNullOrWhiteSpace(farewell))
        {
            return null;
        }

        return OutgoingMessage.Reply(farewell, context.MessageId);
    }
}
