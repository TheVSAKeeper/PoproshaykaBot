namespace PoproshaykaBot.Core.Chat.Commands;

public static class CommandResponseTargetExtensions
{
    public static bool GoesToChat(this CommandResponseTarget target)
    {
        return (target & (CommandResponseTarget.Chat | CommandResponseTarget.Caller)) != CommandResponseTarget.None;
    }

    public static bool RepliesToCaller(this CommandResponseTarget target)
    {
        return target.HasFlag(CommandResponseTarget.Caller);
    }

    public static bool GoesToOverlay(this CommandResponseTarget target)
    {
        return target.HasFlag(CommandResponseTarget.Overlay);
    }
}
