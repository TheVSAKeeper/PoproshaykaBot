using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.Infrastructure;

internal static class BroadcastDeliveryText
{
    public static string? Format(
        IReadOnlyDictionary<StreamingPlatform, BroadcastPlatformDelivery> results, IChatPlatformRegistry platforms)
    {
        if (results.Count == 0)
        {
            return null;
        }

        var lines = results.OrderBy(entry => entry.Key).Select(entry =>
            $"{PlatformVisuals.GetDisplayName(entry.Key, platforms)}: {Describe(entry.Value)}");
        var accepted = results.Values.Count(result => result.EnqueueResult == ChatEnqueueResult.Queued);
        var confirmed = results.Values.Count(result => result.Delivery?.DeliveryStatus == ChatDeliveryStatus.Sent);
        var hasUnsuccessfulDelivery = results.Values.Any(result =>
            result.Delivery is { DeliveryStatus: not (ChatDeliveryStatus.None or ChatDeliveryStatus.Sent) });
        var partial = (accepted > 0 && accepted < results.Count)
            || (confirmed > 0 && hasUnsuccessfulDelivery)
            || results.Values.Any(result => result.Delivery is { SentParts: > 0, DeliveryStatus: not ChatDeliveryStatus.Sent });
        return (partial ? "Частичный результат. " : string.Empty) + string.Join(" · ", lines);
    }

    private static string Describe(BroadcastPlatformDelivery result)
    {
        if (result.EnqueueResult != ChatEnqueueResult.Queued)
        {
            return ChatEnqueueResults.DescribeRefusal(result.EnqueueResult);
        }

        if (result.Delivery is { SentParts: > 0, DeliveryStatus: not ChatDeliveryStatus.Sent } partial)
        {
            return $"подтверждена часть текста ({partial.SentParts})";
        }

        return result.Delivery?.DeliveryStatus switch
        {
            ChatDeliveryStatus.Sent => "доставка подтверждена",
            ChatDeliveryStatus.ChatUnavailable => "чат стал недоступен",
            ChatDeliveryStatus.AuthorizationRequired => "требуется вход",
            ChatDeliveryStatus.QueueClosed => "очередь закрылась",
            ChatDeliveryStatus.QuotaExhausted => "квота исчерпана",
            ChatDeliveryStatus.ObserverMode => "режим наблюдателя",
            ChatDeliveryStatus.Rejected => "отправка отклонена",
            ChatDeliveryStatus.Unconfirmed => "доставка не подтверждена",
            _ => result.AwaitingConfirmation ? "в очереди" : "принято в очередь, подтверждение недоступно",
        };
    }
}
