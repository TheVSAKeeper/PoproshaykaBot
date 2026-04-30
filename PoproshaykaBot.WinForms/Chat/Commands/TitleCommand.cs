using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class TitleCommand(IChannelInformationApplier applier, ILogger<TitleCommand> logger) : IChatCommand
{
    public string Canonical => "title";
    public IReadOnlyCollection<string> Aliases => ["название"];
    public string Description => "обновить название трансляции";

    public bool CanExecute(CommandContext context)
    {
        return context.IsBroadcaster || context.IsModerator;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return OutgoingMessage.Reply("Использование: !title <текст>", context.MessageId);
        }

        var title = string.Join(" ", context.Arguments);

        _ = Task.Run(async () =>
        {
            try
            {
                await applier.ApplyPatchAsync(title, null, null, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Не удалось обновить название");
            }
        });

        return OutgoingMessage.Reply("Название обновлено", context.MessageId);
    }
}
