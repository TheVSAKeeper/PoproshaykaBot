namespace PoproshaykaBot.WinForms.Infrastructure.Events.Logging;

public enum BotLogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

public sealed record BotLogEntry(BotLogLevel Level, string Source, string Message) : EventBase;
