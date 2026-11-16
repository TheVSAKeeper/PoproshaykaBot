namespace PoproshaykaBot.Core.Twitch.Chat;

public enum ChatEnqueueResult
{
    Queued = 0,
    EmptyMessage = 1,
    ObserverMode = 2,
    QueueFull = 3,
    QueueClosed = 4,
    NotConnected = 5,
    QuotaBudgetReached = 6,
}
