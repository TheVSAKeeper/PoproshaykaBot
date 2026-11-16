using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Infrastructure.Logging;

internal static partial class CoreLog
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "Переключение чтения чата YouTube с InnerTube на официальный транспорт ({ErrorKind}), охлаждение {CooldownMinutes} мин")]
    public static partial void InnerTubeFallbackActivated(this ILogger logger, string errorKind, int cooldownMinutes);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Охлаждение InnerTube истекло, возвращаем приоритетное чтение для видео {VideoId}")]
    public static partial void InnerTubeCooldownExpired(this ILogger logger, string? videoId);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Debug,
        Message = "Пропуск InnerTube для видео {VideoId}: аккаунт бота совпадает с каналом стримера")]
    public static partial void InnerTubeSkippedSharedAccount(this ILogger logger, string? videoId);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message = "Сброс курсора InnerTube для видео {VideoId}: {Reason}")]
    public static partial void InnerTubeCursorReset(this ILogger logger, string? videoId, string reason);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Warning,
        Message = "YouTube чат {LiveChatId}: смена несовместимого транспорта на {Transport}, курсор сброшен")]
    public static partial void YouTubeIncompatibleTransportSwitch(this ILogger logger, string liveChatId, string transport);

    [LoggerMessage(
        EventId = 2105,
        Level = LogLevel.Information,
        Message = "YouTube чат {LiveChatId} завершён в InnerTube, обновляем статус трансляции")]
    public static partial void YouTubeInnerTubeChatEnded(this ILogger logger, string liveChatId);

    [LoggerMessage(
        EventId = 2106,
        Level = LogLevel.Warning,
        Message = "Повреждённый файл кэша чата {FileName} сохранён в копию invalid и заморожен на текущий сеанс")]
    public static partial void ChatHistoryCacheCorrupt(this ILogger logger, string fileName);

    [LoggerMessage(
        EventId = 2107,
        Level = LogLevel.Warning,
        Message = "Не удалось записать файл кэша чата {FileName} – повторная попытка в фоне")]
    public static partial void ChatHistoryCacheWriteFailed(this ILogger logger, Exception exception, string fileName);

    [LoggerMessage(
        EventId = 2108,
        Level = LogLevel.Warning,
        Message = "Не удалось удалить файл кэша чата или служебную копию {FileName}")]
    public static partial void ChatHistoryCacheDeleteFailed(this ILogger logger, Exception exception, string fileName);

    [LoggerMessage(
        EventId = 2109,
        Level = LogLevel.Warning,
        Message = "Внешняя запись в кэш истории чата оборвалась – запись кэша до перезапуска отключена")]
    public static partial void ChatHistoryCacheExternalWriteAbandoned(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2110,
        Level = LogLevel.Information,
        Message = "Кэш истории чата восстановлен: областей {ScopeCount}, сообщений {MessageCount}")]
    public static partial void ChatHistoryCacheRestored(this ILogger logger, int scopeCount, int messageCount);
}
