using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace PoproshaykaBot.Core.Streaming;

public static class StreamingErrorMessages
{
    public static string SafeMessage(Exception exception)
    {
        return SafeMessage(exception, StreamingPlatform.Twitch);
    }

    public static string SafeMessage(Exception exception, StreamingPlatform platform)
    {
        if (exception is YouTubeApiException ytEx)
        {
            return SafeYouTubeMessage(ytEx);
        }

        if (exception is YouTubeOAuthException ytOAuthEx)
        {
            return SafeYouTubeOAuthMessage(ytOAuthEx);
        }

        if (platform == StreamingPlatform.Twitch)
        {
            return exception switch
            {
                WebSocketException or SocketException => "соединение с EventSub прервано",
                JsonException => "некорректный ответ Twitch",
                _ => HelixErrorMessages.SafeMessage(exception),
            };
        }

        var platformName = platform.ToString();
        return exception switch
        {
            OperationCanceledException => "Операция отменена",
            TimeoutException => $"Превышено время ожидания ответа {platformName}",
            HttpRequestException or WebSocketException or SocketException => $"Ошибка сети при обращении к {platformName}",
            UnauthorizedAccessException => $"Ошибка авторизации в {platformName}",
            JsonException => $"Некорректный ответ {platformName}",
            _ => $"Ошибка при обращении к {platformName}.",
        };
    }

    private static string SafeYouTubeMessage(YouTubeApiException exception)
    {
        return exception.Kind switch
        {
            YouTubeApiErrorKind.Unauthorized => "Нет авторизации YouTube. Проверьте вход для площадки.",
            YouTubeApiErrorKind.Forbidden => "Недостаточно прав YouTube для запроса.",
            YouTubeApiErrorKind.QuotaExceeded or YouTubeApiErrorKind.LocalQuotaBudgetReached
                => "Дневной лимит квоты YouTube исчерпан.",
            YouTubeApiErrorKind.RateLimitExceeded => "Слишком много запросов к YouTube. Попробуйте чуть позже.",
            YouTubeApiErrorKind.NotFound => "Канал или ресурс YouTube не найден.",
            YouTubeApiErrorKind.BadRequest => "YouTube отклонил запрос.",
            YouTubeApiErrorKind.Transient => "Временная ошибка YouTube API. Попробуйте ещё раз.",
            YouTubeApiErrorKind.TransportUnsupported => "Транспорт YouTube API не поддерживается.",
            _ => "Ошибка при обращении к YouTube.",
        };
    }

    private static string SafeYouTubeOAuthMessage(YouTubeOAuthException exception)
    {
        return exception.ErrorCode switch
        {
            "access_denied" => "Доступ к YouTube не подтверждён.",
            "missing_required_scopes" => "Не выданы обязательные права YouTube.",
            _ => "Ошибка авторизации YouTube.",
        };
    }
}
