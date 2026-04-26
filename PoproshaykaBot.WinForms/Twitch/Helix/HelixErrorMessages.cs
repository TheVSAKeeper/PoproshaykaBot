using System.Net;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public static class HelixErrorMessages
{
    public static string SafeMessage(Exception exception)
    {
        if (exception is HelixRequestException helixEx)
        {
            var twitchMessage = helixEx.TwitchErrorMessage ?? string.Empty;

            if (twitchMessage.Contains("not a partner or affiliate", StringComparison.OrdinalIgnoreCase))
            {
                return "Эта функция Twitch доступна только каналам со статусом Partner или Affiliate.";
            }

            return helixEx.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => "Недостаточно прав Twitch для запроса. Проверь авторизацию.",
                HttpStatusCode.NotFound
                    => "Ресурс Twitch не найден.",
                HttpStatusCode.TooManyRequests
                    => "Слишком много запросов. Попробуй чуть позже.",
                _ => "Не удалось выполнить запрос к Twitch. Попробуй ещё раз.",
            };
        }

        return exception switch
        {
            OperationCanceledException => "Операция отменена",
            TimeoutException => "Превышено время ожидания ответа Twitch",
            HttpRequestException => "Ошибка сети при обращении к Twitch",
            UnauthorizedAccessException => "Ошибка авторизации в Twitch",
            TwitchAuthorizationMissingException authMissing => authMissing.Message,
            _ => "Twitch отклонил запрос",
        };
    }
}
