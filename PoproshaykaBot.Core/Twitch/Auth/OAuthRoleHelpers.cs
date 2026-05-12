using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Twitch.Auth;

internal static class OAuthRoleHelpers
{
    public static string DescribeRole(TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => "бот",
            TwitchOAuthRole.Broadcaster => "стример",
            _ => role.ToString(),
        };
    }

    public static string GetExpectedLogin(TwitchSettings twitch, TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => string.Empty,
            TwitchOAuthRole.Broadcaster => twitch.Channel ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    public static void ClearAccountInPlace(TwitchAccountSettings account)
    {
        account.AccessToken = string.Empty;
        account.RefreshToken = string.Empty;
        account.Login = string.Empty;
        account.UserId = string.Empty;
        account.StoredScopes = [];
        account.AccessTokenExpiresAt = null;
    }

    public static void VerifyLoginMatchesRole(
        TwitchOAuthRole role,
        TokenValidationInfo validation,
        SettingsManager settingsManager,
        ILogger logger,
        bool checkBroadcasterChannel = true)
    {
        if (!checkBroadcasterChannel && role == TwitchOAuthRole.Broadcaster)
        {
            return;
        }

        var twitch = settingsManager.Current.Twitch;
        var expected = GetExpectedLogin(twitch, role);

        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        if (string.Equals(expected, validation.Login, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var description = DescribeRole(role);
        var message = $"Авторизация для роли {description} получила токен пользователя '{validation.Login}', но в настройках указан канал '{expected}'. Авторизуйтесь под нужной учёткой или исправьте настройку канала. Токен не сохранён.";

        logger.LogError("Логин в полученном токене ({Login}) не совпадает с ожидаемым ({Expected}) для роли {Role}",
            validation.Login, expected, role);

        throw new InvalidOperationException(message);
    }
}
