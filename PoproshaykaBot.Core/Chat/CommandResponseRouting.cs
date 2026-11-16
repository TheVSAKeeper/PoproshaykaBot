using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat;

public enum CommandDeliveryAction
{
    None = 0,
    Send = 1,
    ReplyThread = 2,
    MentionSend = 3,
    Whisper = 4,
    OverlayOnly = 5,
}

public readonly record struct CommandDeliveryDecision(CommandDeliveryAction Action, string? ReplyToMessageId = null);

public static class CommandResponseRouting
{
    public static CommandDeliveryDecision Resolve(
        CommandResponseTarget target,
        OutgoingMessage response,
        string incomingMessageId,
        PlatformCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (target == CommandResponseTarget.None)
        {
            return new(CommandDeliveryAction.None);
        }

        if (target.WhispersToCaller())
        {
            return (capabilities & PlatformCapabilities.Whisper) != PlatformCapabilities.None
                ? new(CommandDeliveryAction.Whisper)
                : ResolveCallerReply(incomingMessageId, capabilities);
        }

        if (target.GoesToChat())
        {
            if (target.RepliesToCaller())
            {
                return ResolveCallerReply(incomingMessageId, capabilities);
            }

            if (response.Delivery == DeliveryType.Reply && (capabilities & PlatformCapabilities.Reply) != PlatformCapabilities.None)
            {
                return new(CommandDeliveryAction.ReplyThread, response.ReplyToMessageId ?? incomingMessageId);
            }

            return new(CommandDeliveryAction.Send);
        }

        return new(CommandDeliveryAction.OverlayOnly);
    }

    public static bool WillDeliverToChat(CommandResponseTarget target, PlatformCapabilities capabilities)
    {
        if (target == CommandResponseTarget.None)
        {
            return false;
        }

        if (target.WhispersToCaller())
        {
            return (capabilities & PlatformCapabilities.Whisper) == PlatformCapabilities.None;
        }

        return target.GoesToChat();
    }

    public static string PrependCallerMention(string callerDisplayName, string text)
    {
        var normalized = (callerDisplayName ?? string.Empty).Trim().TrimStart('@');

        if (string.IsNullOrEmpty(normalized))
        {
            return text;
        }

        return string.IsNullOrEmpty(text)
            ? $"@{normalized}"
            : $"@{normalized} {text}";
    }

    private static CommandDeliveryDecision ResolveCallerReply(string incomingMessageId, PlatformCapabilities capabilities)
    {
        return (capabilities & PlatformCapabilities.Reply) != PlatformCapabilities.None
            ? new(CommandDeliveryAction.ReplyThread, incomingMessageId)
            : new(CommandDeliveryAction.MentionSend);
    }
}
