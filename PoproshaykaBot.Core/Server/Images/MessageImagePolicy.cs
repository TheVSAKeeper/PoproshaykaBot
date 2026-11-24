using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Server.Images;

public sealed record MessageImage(string Url, int StartIndex, int EndIndex);

public static class MessageImagePolicy
{
    public const string ProxyPath = "/api/image";
    public const string UrlQueryKey = "u";

    public static IReadOnlyList<MessageImage> BuildImages(ChatMessageData message, ObsChatSettings settings)
    {
        return TryBuildImageLink(message, settings, out var link)
            ? [new(BuildProxyUrl(link.Uri), link.StartIndex, link.EndIndex)]
            : [];
    }

    public static bool TryBuildImageLink(ChatMessageData message, ObsChatSettings settings, out MessageImageLink link)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);

        return TryBuildImageLink(message.MessageType, message.Status, message.IsBot, message.Message, settings, out link);
    }

    public static bool TryBuildImageLink(
        ChatMessageType messageType,
        UserStatus status,
        bool isBot,
        string? message,
        ObsChatSettings settings,
        out MessageImageLink link)
    {
        ArgumentNullException.ThrowIfNull(settings);

        link = null!;

        if (!settings.ShowMessageImages || messageType == ChatMessageType.SystemNotification)
        {
            return false;
        }

        if (!IsSenderAllowed(messageType, status, isBot, settings.MessageImageRoles))
        {
            return false;
        }

        return MessageImageLinkDetector.TryFindFirst(message, settings.MessageImageAllowedHosts, out link);
    }

    public static string BuildProxyUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return $"{ProxyPath}?{UrlQueryKey}={Uri.EscapeDataString(uri.AbsoluteUri)}";
    }

    public static bool IsSenderAllowed(UserStatus status, MessageImageSenderRoles roles)
    {
        if (roles.HasFlag(MessageImageSenderRoles.Everyone))
        {
            return true;
        }

        return (status.HasFlag(UserStatus.Broadcaster) && roles.HasFlag(MessageImageSenderRoles.Broadcaster))
            || (status.HasFlag(UserStatus.Moderator) && roles.HasFlag(MessageImageSenderRoles.Moderator))
            || (status.HasFlag(UserStatus.Vip) && roles.HasFlag(MessageImageSenderRoles.Vip))
            || (status.HasFlag(UserStatus.Subscriber) && roles.HasFlag(MessageImageSenderRoles.Subscriber));
    }

    private static bool IsSenderAllowed(ChatMessageType messageType, UserStatus status, bool isBot, MessageImageSenderRoles roles)
    {
        if (messageType == ChatMessageType.BotResponse || isBot)
        {
            return roles.HasFlag(MessageImageSenderRoles.Everyone)
                || roles.HasFlag(MessageImageSenderRoles.Bot)
                || (status.HasFlag(UserStatus.Broadcaster) && roles.HasFlag(MessageImageSenderRoles.Broadcaster));
        }

        return IsSenderAllowed(status, roles);
    }
}
