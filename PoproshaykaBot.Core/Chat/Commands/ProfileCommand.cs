using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class ProfileCommand(BroadcastProfilesManager manager, ILogger<ProfileCommand> logger) : IChatCommand
{
    public string Canonical => "profile";
    public IReadOnlyCollection<string> Aliases => ["профиль"];
    public string Description => "применить сохранённый профиль трансляции";

    public bool CanExecute(CommandContext context)
    {
        return context.IsBroadcaster || context.IsModerator;
    }

    public OutgoingMessage? Execute(CommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return OutgoingMessage.Reply("Использование: !profile <имя>", context.MessageId);
        }

        var name = string.Join(" ", context.Arguments);
        var profile = manager.FindByName(name);

        if (profile == null)
        {
            return OutgoingMessage.Reply($"""Профиль "{name}" не найден""", context.MessageId);
        }

        var resolvedId = profile.Id;
        var resolvedName = profile.Name;

        _ = Task.Run(async () =>
        {
            try
            {
                await manager.ApplyAsync(resolvedId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Не удалось применить профиль '{Name}' из чата", resolvedName);
            }
        });

        return OutgoingMessage.Reply($"Применяю профиль: {resolvedName}", context.MessageId);
    }
}
