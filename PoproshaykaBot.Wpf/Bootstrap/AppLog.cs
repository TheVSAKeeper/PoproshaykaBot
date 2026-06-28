using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Wpf.Bootstrap;

internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Ошибка завершения работы при закрытии окна")]
    public static partial void ShutdownOnCloseFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Ошибка подключения бота")]
    public static partial void BotConnectFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Ошибка при отключении бота")]
    public static partial void BotDisconnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Ошибка запуска подключения")]
    public static partial void BotStartConnectionFailed(this ILogger logger, Exception exception);
}
