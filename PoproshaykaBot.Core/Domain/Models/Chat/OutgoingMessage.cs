namespace PoproshaykaBot.Core.Domain.Models.Chat;

public sealed class OutgoingMessage
{
    public string Text { get; init; } = string.Empty;
    public DeliveryType Delivery { get; init; }
    public string? ReplyToMessageId { get; init; }

    public static OutgoingMessage Normal(string text)
    {
        return new()
        {
            Text = text,
            Delivery = DeliveryType.Normal,
        };
    }

    public static OutgoingMessage Reply(string text, string? replyToMessageId)
    {
        return new()
        {
            Text = text,
            Delivery = DeliveryType.Reply,
            ReplyToMessageId = replyToMessageId,
        };
    }
}