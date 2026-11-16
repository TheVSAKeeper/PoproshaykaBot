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
}
