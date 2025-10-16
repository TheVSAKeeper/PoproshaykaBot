﻿using PoproshaykaBot.Core.Domain.Models.Chat;

namespace PoproshaykaBot.Core.Application.Chat.Commands.Implementations;

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
