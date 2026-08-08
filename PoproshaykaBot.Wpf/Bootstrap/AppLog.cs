using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

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

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "OAuth-поток мастера упал для роли {Role}")]
    public static partial void OAuthFlowFailed(this ILogger logger, Exception? exception, TwitchOAuthRole role);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "Не удалось сохранить настройки и токены перед подключением бота")]
    public static partial void BotConnectionSaveSettingsFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Запуск подключения отклонён: уже выполняется")]
    public static partial void BotConnectionStartRejected(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Error, Message = "Не удалось сохранить настройки в onboarding-мастере")]
    public static partial void OnboardingSettingsSaveFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning, Message = "Авто-подключение бота отклонено")]
    public static partial void OnboardingAutoConnectRejected(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Error, Message = "Сбой проверок на CompletionPage")]
    public static partial void OnboardingCompletionCheckFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Debug, Message = "Сбой проверки канала {Channel}")]
    public static partial void OnboardingChannelCheckFailed(this ILogger logger, Exception? exception, string channel);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Error, Message = "Не удалось перезапустить HTTP сервер на порту {Port}")]
    public static partial void OnboardingHttpServerRestartFailed(this ILogger logger, Exception? exception, int port);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning, Message = "Ошибка при проверке Client ID и Secret")]
    public static partial void OnboardingCredentialsCheckFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "Ошибка при проверке канала @{Channel}")]
    public static partial void OnboardingChannelCredentialsCheckFailed(this ILogger logger, Exception? exception, string channel);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Warning, Message = "Сбой постановки тестового сообщения в очередь отправки")]
    public static partial void OnboardingChatTestEnqueueFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Warning, Message = "Порт {Port} недоступен для биндинга в onboarding")]
    public static partial void OnboardingPortUnavailable(this ILogger logger, int port);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Information, Message = "HTTP сервер перезапущен на порту {Port} в onboarding")]
    public static partial void OnboardingHttpServerRestarted(this ILogger logger, int port);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Error, Message = "Ошибка запуска HTTP сервера на порту {Port} в onboarding")]
    public static partial void OnboardingHttpServerStartFailed(this ILogger logger, Exception? exception, int port);

    [LoggerMessage(EventId = 1117, Level = LogLevel.Error, Message = "Не удалось восстановить HTTP сервер на старом порту {Port}")]
    public static partial void OnboardingHttpServerRestoreFailed(this ILogger logger, Exception? exception, int port);

    [LoggerMessage(EventId = 1118, Level = LogLevel.Error, Message = "Ошибка освобождения ресурсов страницы мастера {PageType}")]
    public static partial void OnboardingPageDisposeFailed(this ILogger logger, Exception? exception, string pageType);

    [LoggerMessage(EventId = 1119, Level = LogLevel.Information, Message = "Бот остановлен после отмены мастера первичной настройки")]
    public static partial void OnboardingBotStopped(this ILogger logger);

    [LoggerMessage(EventId = 1120, Level = LogLevel.Error, Message = "Не удалось остановить бота при откате мастера")]
    public static partial void OnboardingBotStopFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Information, Message = "Аккаунты Twitch откачены на исходные значения после отмены мастера")]
    public static partial void OnboardingAccountsRolledBack(this ILogger logger);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Error, Message = "Не удалось восстановить аккаунты Twitch после отмены мастера")]
    public static partial void OnboardingAccountsRollbackFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1123, Level = LogLevel.Information, Message = "Канал откачен на исходное значение «{Channel}» после отмены мастера")]
    public static partial void OnboardingChannelRolledBack(this ILogger logger, string channel);

    [LoggerMessage(EventId = 1124, Level = LogLevel.Information, Message = "HTTP сервер откачен на исходный порт {Port} после отмены мастера")]
    public static partial void OnboardingHttpServerRolledBack(this ILogger logger, int port);

    [LoggerMessage(EventId = 1125, Level = LogLevel.Error, Message = "Не удалось откатить HTTP сервер на порт {Port} после отмены мастера")]
    public static partial void OnboardingHttpServerRollbackFailed(this ILogger logger, Exception? exception, int port);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Error, Message = "Ошибка сохранения настроек")]
    public static partial void SettingsSaveFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Некорректный RedirectUri '{RedirectUri}' — порт HTTP сервера не обновлён")]
    public static partial void SettingsRedirectUriInvalid(this ILogger logger, string redirectUri);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Порт HTTP сервера обновлён с {OldPort} на {NewPort} в соответствии с RedirectUri")]
    public static partial void SettingsHttpPortUpdated(this ILogger logger, int oldPort, int newPort);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "Перезапуск HTTP сервера: порт {OldPort} -> {NewPort}")]
    public static partial void SettingsHttpServerRestarting(this ILogger logger, int oldPort, int newPort);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error, Message = "Ошибка перезапуска HTTP сервера на порту {NewPort}")]
    public static partial void SettingsHttpServerRestartFailed(this ILogger logger, Exception? exception, int newPort);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Error, Message = "Ошибка обновления информации о стриме")]
    public static partial void StreamInfoUpdateFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "Ошибка автообновления информации о стриме")]
    public static partial void StreamInfoAutoUpdateFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "Добавление профиля рассылки: не реализовано")]
    public static partial void BroadcastProfileAddNotImplemented(this ILogger logger);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Information, Message = "Редактирование текущих настроек канала: не реализовано")]
    public static partial void BroadcastProfileEditNotImplemented(this ILogger logger);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "Не удалось положить лог-секцию в буфер обмена")]
    public static partial void SupportClipboardCopyFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Error, Message = "Не удалось открыть форму репорта")]
    public static partial void SupportReportFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Не удалось выполнить OBS-действие для карточки {Kind}")]
    public static partial void ObsOutputActionFailed(this ILogger logger, Exception? exception, ObsOutputCardKind kind);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Чат-источники для refresh не настроены")]
    public static partial void ObsChatSourcesNotConfigured(this ILogger logger);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning, Message = "Ручной refresh: OBS отклонил все {Count} запросов, проверьте имена источников")]
    public static partial void ObsChatSourcesAllRejected(this ILogger logger, int count);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning, Message = "Ручной refresh чат-источников OBS: превышено время ожидания")]
    public static partial void ObsChatSourcesRefreshTimeout(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning, Message = "Не удалось обновить чат-источники OBS вручную")]
    public static partial void ObsChatSourcesRefreshFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1405, Level = LogLevel.Warning, Message = "Не удалось обновить плитку OBS")]
    public static partial void ObsTileRefreshFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1407, Level = LogLevel.Warning, Message = "Ошибка операции OBS-интеграции")]
    public static partial void ObsOperationFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1408, Level = LogLevel.Debug, Message = "Не удалось загрузить список сцен OBS для привязки профиля")]
    public static partial void ObsScenesLoadFailed(this ILogger logger, Exception? exception);
}
