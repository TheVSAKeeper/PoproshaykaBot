using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Server.Images;

public static class MessageImagePolicy
{
    public const string ProxyPath = "/api/image";
    public const string UrlQueryKey = "u";

    public static IReadOnlyList<string> BuildImageUrls(ChatMessageData message, ObsChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.ShowMessageImages || message.MessageType != ChatMessageType.UserMessage)
        {
            return [];
        }

        if (!IsSenderAllowed(message.Status, settings.MessageImageRoles))
        {
            return [];
        }

        if (!MessageImageLinkDetector.TryFindFirst(message.Message, settings.MessageImageAllowedHosts, out var uri))
        {
            return [];
        }

        return [BuildProxyUrl(uri)];
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
}
