namespace PoproshaykaBot.WinForms.Server;

public readonly record struct SseEnvelope(string? EventType, string Payload)
{
    public bool IsComment => EventType is null;

    public static SseEnvelope Comment(string text)
    {
        return new(null, text);
    }
}
