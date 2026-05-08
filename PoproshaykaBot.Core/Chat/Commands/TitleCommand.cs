using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class TitleCommand(IChannelInformationApplier applier, ILogger<TitleCommand> logger) : IChatCommand
{
    public string Canonical => "title";
    public IReadOnlyCollection<string> Aliases => ["название"];
    public string Description => "обновить название трансляции";

    public bool CanExecute(CommandContext context)
    {
        return context.IsBroadcaster || context.IsModerator;
    }

    public async Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count == 0)
        {
            return OutgoingMessage.Reply("Использование: !title <текст>", context.MessageId);
        }

        var title = string.Join(" ", context.Arguments);

        try
        {
            await applier.ApplyPatchAsync(title, null, null, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось обновить название");
            return OutgoingMessage.Reply("Не удалось обновить название", context.MessageId);
        }

        return OutgoingMessage.Reply("Название обновлено", context.MessageId);
    }
}
