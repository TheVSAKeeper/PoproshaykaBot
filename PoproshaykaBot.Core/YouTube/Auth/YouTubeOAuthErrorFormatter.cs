using System.Net;

namespace PoproshaykaBot.Core.YouTube.Auth;

public static class YouTubeOAuthErrorFormatter
{
    public static string DescribeSafe(YouTubeOAuthException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ErrorCode switch
        {
            "access_denied" => "доступ к Google-аккаунту отклонён пользователем",
            "missing_required_scopes" => "не выданы обязательные права YouTube на экране согласия Google",
            "invalid_client" => "неверный Client ID или Client Secret Google-проекта",
            "invalid_grant" => "токен или код подтверждения отклонён Google, повторите вход",
            "accessNotConfigured" => "в проекте Google Cloud не включён YouTube Data API v3",
            "quotaExceeded" => "исчерпана суточная квота YouTube Data API v3",
            "youtubeSignupRequired" => "у выбранного Google-аккаунта нет канала YouTube",
            _ => exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "отказ авторизации Google, проверьте настройки клиента и права"
                : "ошибка авторизации YouTube, проверьте настройки Google-проекта и подключение",
        };
    }

    public static string DescribeSafe(Exception exception)
    {
        return exception is YouTubeOAuthException oauthException
            ? DescribeSafe(oauthException)
            : "ошибка авторизации YouTube, проверьте настройки Google-проекта и подключение";
    }
}
