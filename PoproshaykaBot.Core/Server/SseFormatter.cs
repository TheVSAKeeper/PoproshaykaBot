namespace PoproshaykaBot.Core.Server;

public static class SseFormatter
{
    public static string Format(SseEnvelope envelope)
    {
        if (envelope.IsComment)
        {
            return $": {envelope.Payload}\n\n";
        }

        var eventType = envelope.EventType!;

        if (eventType.Contains('\n') || eventType.Contains('\r'))
        {
            throw new ArgumentException("Event type не должен содержать перевод строки.", nameof(envelope));
        }

        return $"event: {eventType}\ndata: {envelope.Payload}\n\n";
    }
}
