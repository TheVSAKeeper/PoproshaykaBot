using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class GameCommand(
    IChannelInformationApplier applier,
    IGameCategoryResolver resolver,
    ILogger<GameCommand> logger)
    : IChatCommand
{
    public string Canonical => "game";
    public IReadOnlyCollection<string> Aliases => ["игра", "категория"];
    public string Description => "обновить категорию трансляции";

    public bool CanExecute(CommandContext context)
    {
        return context.IsBroadcaster || context.IsModerator;
    }

    public OutgoingMessage? Execute(CommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return OutgoingMessage.Reply("Использование: !game <название>", context.MessageId);
        }

        var query = string.Join(" ", context.Arguments);

        _ = Task.Run(async () =>
        {
            try
            {
                var suggestion = await resolver.ResolveAsync(query, CancellationToken.None);

                if (suggestion == null)
                {
                    logger.LogInformation("Категория '{Query}' не найдена", query);
                    return;
                }

                await applier.ApplyPatchAsync(null,
                    suggestion.Id,
                    suggestion.Name,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Не удалось обновить категорию");
            }
        });

        return OutgoingMessage.Reply($"Применяю категорию: {query}", context.MessageId);
    }
}
