using PoproshaykaBot.Core.Application.Bot;

namespace PoproshaykaBot.Core.Domain.Models.Connection;

public class BotConnectionResult
{
    private BotConnectionResult(BotConnectionStatus status, Bot? bot = null, Exception? exception = null)
    {
        Status = status;
        Bot = bot;
        Exception = exception;
    }

    public Bot? Bot { get; }

    public Exception? Exception { get; }

    public string? ErrorMessage => Exception?.Message;

    public bool IsSuccess => Status == BotConnectionStatus.Success;

    public bool IsCancelled => Status == BotConnectionStatus.Cancelled;

    public bool IsFailed => Status == BotConnectionStatus.Failed;

    private BotConnectionStatus Status { get; }

    public static BotConnectionResult Success(Bot bot)
    {
        return new(BotConnectionStatus.Success, bot);
    }

    public static BotConnectionResult Cancelled()
    {
        return new(BotConnectionStatus.Cancelled);
    }

    public static BotConnectionResult Failed(Exception exception)
    {
        return new(BotConnectionStatus.Failed, exception: exception);
    }
}
