using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Platforms;
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

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "История сообщений чата очищена")]
    public static partial void ChatHistoryCleared(this ILogger logger);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "История сообщений чата очищена в памяти, но часть файлов кэша на диске не удалось обновить или удалить")]
    public static partial void ChatHistoryClearIncomplete(this ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Пользователь запросил подключение бота")]
    public static partial void BotConnectRequested(this ILogger logger);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Пользователь запросил отключение бота")]
    public static partial void BotDisconnectRequested(this ILogger logger);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Агент по MCP запросил подключение бота")]
    public static partial void McpBotConnectRequested(this ILogger logger);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Агент по MCP запросил отключение бота")]
    public static partial void McpBotDisconnectRequested(this ILogger logger);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Пользователь запросил перезапуск приложения")]
    public static partial void RestartRequested(this ILogger logger);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Пользователь запросил принудительный перезапуск приложения")]
    public static partial void ForcedRestartRequested(this ILogger logger);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Пользователь отказался от принудительного перезапуска")]
    public static partial void ForcedRestartDeclined(this ILogger logger);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Перезапуск недоступен в режиме без окна (smoke-тест, галерея)")]
    public static partial void RestartUnavailableHeadless(this ILogger logger);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Перезапуск отменён: закрытие окна не подтверждено")]
    public static partial void RestartCancelled(this ILogger logger);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "Пользователь запросил подключение площадки {Platform}")]
    public static partial void PlatformConnectRequested(this ILogger logger, string platform);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "Пользователь запросил отключение площадки {Platform}")]
    public static partial void PlatformDisconnectRequested(this ILogger logger, string platform);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Error, Message = "Ошибка запуска подключения площадки {Platform}")]
    public static partial void PlatformStartConnectionFailed(this ILogger logger, Exception exception, string platform);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Error, Message = "Ошибка отключения площадки {Platform}")]
    public static partial void PlatformDisconnectFailed(this ILogger logger, Exception exception, string platform);

    [LoggerMessage(EventId = 1019, Level = LogLevel.Warning, Message = "Частичное подключение бота: работают {ConnectedPlatforms}, сбой {FailedPlatforms}")]
    public static partial void BotPartialConnectionFailure(this ILogger logger, string connectedPlatforms, string failedPlatforms);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information, Message = "По MCP запрошено подключение площадки {Platform}")]
    public static partial void McpPlatformConnectRequested(this ILogger logger, StreamingPlatform platform);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Information, Message = "По MCP запрошено отключение площадки {Platform}")]
    public static partial void McpPlatformDisconnectRequested(this ILogger logger, StreamingPlatform platform);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Information, Message = "По MCP запрошено обновление мониторинга (площадка: {Platform}, канал: {ChannelId})")]
    public static partial void McpMonitoringRefreshRequested(this ILogger logger, StreamingPlatform? platform, string? channelId);

    [LoggerMessage(EventId = 1023, Level = LogLevel.Information, Message = "По MCP запрошено переключение активного канала {Platform} на {ChannelId}")]
    public static partial void McpActiveChannelSwitchRequested(this ILogger logger, StreamingPlatform platform, string channelId);

    [LoggerMessage(EventId = 1024, Level = LogLevel.Information, Message = "По MCP запрошено обновление состояния OBS (connectIfNeeded={ConnectIfNeeded})")]
    public static partial void McpObsRefreshRequested(this ILogger logger, bool connectIfNeeded);

    [LoggerMessage(EventId = 1025, Level = LogLevel.Information, Message = "По MCP запрошено сохранение статистики на диск")]
    public static partial void McpStatisticsFlushRequested(this ILogger logger);

    [LoggerMessage(EventId = 1026, Level = LogLevel.Information, Message = "По MCP экспортирован пакет диагностики: {Path} (редакция полная: {RedactionComplete})")]
    public static partial void McpDiagnosticsBundleExported(this ILogger logger, string path, bool redactionComplete);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "OAuth-поток мастера упал для роли {Role}")]
    public static partial void OAuthFlowFailed(this ILogger logger, Exception? exception, PlatformAuthRole role);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "Не удалось сохранить настройки и токены перед подключением бота")]
    public static partial void BotConnectionSaveSettingsFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Запуск подключения отклонён: уже выполняется")]
    public static partial void BotConnectionStartRejected(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1133, Level = LogLevel.Warning, Message = "Шаг подключения остановлен. Настройки мастера не записаны")]
    public static partial void BotConnectionSettingsNotWritten(this ILogger logger);

    [LoggerMessage(EventId = 1135, Level = LogLevel.Warning, Message = "Шаг подключения остановлен. Вход не сохранён")]
    public static partial void BotConnectionAccountsNotWritten(this ILogger logger);

    [LoggerMessage(EventId = 1136, Level = LogLevel.Information, Message = "Аккаунты возвращены к состоянию до мастера в памяти, файл accounts.json не переписан: туда перенесены данные предыдущей версии")]
    public static partial void OnboardingAccountsRolledBackInMemory(this ILogger logger);

    [LoggerMessage(EventId = 1134, Level = LogLevel.Warning, Message = "Настройка не завершена. {Reason}")]
    public static partial void OnboardingCompletionNotWritten(this ILogger logger, string reason);

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

    [LoggerMessage(EventId = 1121, Level = LogLevel.Information, Message = "Отменены изменения мастера в аккаунтах и назначениях Twitch")]
    public static partial void OnboardingAccountsRolledBack(this ILogger logger);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Error, Message = "Не удалось восстановить аккаунты Twitch после отмены мастера")]
    public static partial void OnboardingAccountsRollbackFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1124, Level = LogLevel.Information, Message = "HTTP сервер откачен на исходный порт {Port} после отмены мастера")]
    public static partial void OnboardingHttpServerRolledBack(this ILogger logger, int port);

    [LoggerMessage(EventId = 1125, Level = LogLevel.Error, Message = "Не удалось откатить HTTP сервер на порт {Port} после отмены мастера")]
    public static partial void OnboardingHttpServerRollbackFailed(this ILogger logger, Exception? exception, int port);

    [LoggerMessage(EventId = 1126, Level = LogLevel.Error, Message = "Ошибка показа мастера первичной настройки")]
    public static partial void OnboardingWizardShowFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1127, Level = LogLevel.Error, Message = "Встроенная авторизация упала для роли {Role}")]
    public static partial void EmbeddedAuthFlowFailed(this ILogger logger, Exception? exception, PlatformAuthRole role);

    [LoggerMessage(EventId = 1128, Level = LogLevel.Error, Message = "Не удалось поднять встроенный браузер авторизации")]
    public static partial void EmbeddedAuthWebViewFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1129, Level = LogLevel.Warning, Message = "WebView2 Runtime не установлен – встроенная авторизация недоступна")]
    public static partial void EmbeddedAuthRuntimeMissing(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1130, Level = LogLevel.Warning, Message = "Встроенная авторизация подавлена в headless-режиме для роли {Role}")]
    public static partial void EmbeddedAuthSuppressedHeadless(this ILogger logger, PlatformAuthRole role);

    [LoggerMessage(EventId = 1131, Level = LogLevel.Information, Message = "Пользователь начал авторизацию роли {Role} во внешнем браузере ({Surface})")]
    public static partial void OAuthBrowserFlowStarted(this ILogger logger, PlatformAuthRole role, OAuthAuthorizationSurface surface);

    [LoggerMessage(EventId = 1132, Level = LogLevel.Information, Message = "Пользователь начал встроенную авторизацию роли {Role} ({Surface})")]
    public static partial void OAuthEmbeddedFlowStarted(this ILogger logger, PlatformAuthRole role, OAuthAuthorizationSurface surface);

    [LoggerMessage(EventId = 1137, Level = LogLevel.Debug, Message = "Ошибка в уже отменённом или заменённом OAuth-потоке роли {Role}")]
    public static partial void OAuthAbandonedFlowFailed(this ILogger logger, Exception exception, PlatformAuthRole role);

    [LoggerMessage(EventId = 1138, Level = LogLevel.Information, Message = "Пользователь начал авторизацию {Platform} роли {Role} во внешнем браузере ({Surface})")]
    public static partial void PlatformOAuthBrowserFlowStarted(this ILogger logger, string platform, PlatformAuthRole role, OAuthAuthorizationSurface surface);

    [LoggerMessage(EventId = 1139, Level = LogLevel.Information, Message = "Авторизация {Platform} для роли {Role} отменена")]
    public static partial void PlatformOAuthFlowCancelled(this ILogger logger, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1140, Level = LogLevel.Error, Message = "OAuth-поток {Platform} упал для роли {Role}")]
    public static partial void PlatformOAuthFlowFailed(this ILogger logger, Exception? exception, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1141, Level = LogLevel.Information, Message = "Пользователь очистил токены {Platform} для роли {Role}")]
    public static partial void PlatformOAuthTokensCleared(this ILogger logger, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1142, Level = LogLevel.Error, Message = "Не удалось обновить токен {Platform} для роли {Role}")]
    public static partial void PlatformOAuthRefreshFailed(this ILogger logger, Exception? exception, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1143, Level = LogLevel.Warning, Message = "Не удалось проверить токен {Platform} для роли {Role}")]
    public static partial void PlatformOAuthValidationFailed(this ILogger logger, Exception? exception, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1144, Level = LogLevel.Warning, Message = "Не удалось открыть системный браузер для OAuth {Platform} роли {Role}")]
    public static partial void PlatformOAuthBrowserOpenFailed(this ILogger logger, string platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Error, Message = "Ошибка сохранения настроек")]
    public static partial void SettingsSaveFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Некорректный RedirectUri '{RedirectUri}' – порт HTTP сервера не обновлён")]
    public static partial void SettingsRedirectUriInvalid(this ILogger logger, string redirectUri);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Information, Message = "Запрошен вход на площадку {Platform} для роли {Role}")]
    public static partial void ChannelAuthorizationStarted(this ILogger logger, StreamingPlatform platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Information, Message = "Вход на площадку {Platform} для роли {Role} не завершён")]
    public static partial void ChannelAuthorizationIncomplete(this ILogger logger, StreamingPlatform platform, PlatformAuthRole role);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Warning, Message = "Сбой входа на площадку {Platform} для роли {Role} ({ErrorType})")]
    public static partial void ChannelAuthorizationFailed(this ILogger logger, StreamingPlatform platform, PlatformAuthRole role, string errorType);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Information, Message = "Запрошена проверка роли {Role} на канале {ChannelId} площадки {Platform}")]
    public static partial void ChannelRoleVerificationStarted(this ILogger logger, PlatformAuthRole role, string channelId, StreamingPlatform platform);

    [LoggerMessage(EventId = 1217, Level = LogLevel.Warning, Message = "Сбой проверки роли {Role} на канале {ChannelId} площадки {Platform} ({ErrorType})")]
    public static partial void ChannelRoleVerificationFailed(this ILogger logger, PlatformAuthRole role, string channelId, StreamingPlatform platform, string errorType);

    [LoggerMessage(EventId = 1218, Level = LogLevel.Information, Message = "Доступ роли {Role} к каналу {ChannelId} площадки {Platform} не подтверждён")]
    public static partial void ChannelRoleVerificationDenied(this ILogger logger, PlatformAuthRole role, string channelId, StreamingPlatform platform);

    [LoggerMessage(EventId = 1219, Level = LogLevel.Warning, Message = "Не удалось определить канал на площадке {Platform} ({ErrorType})")]
    public static partial void ChannelResolveFailed(this ILogger logger, Exception? exception, StreamingPlatform platform, string errorType);

    [LoggerMessage(EventId = 1220, Level = LogLevel.Information, Message = "Запрошен объединённый вход (бот + стример) на площадку {Platform}")]
    public static partial void CombinedAccountAuthorizationStarted(this ILogger logger, StreamingPlatform platform);

    [LoggerMessage(EventId = 1221, Level = LogLevel.Information, Message = "Пользователь удалил аккаунт {AccountId} на площадке {Platform}")]
    public static partial void PlatformAccountSignedOut(this ILogger logger, StreamingPlatform platform, string accountId);

    [LoggerMessage(EventId = 1222, Level = LogLevel.Information, Message = "Пользователь удалил роль {Role} аккаунта {AccountId} на площадке {Platform}")]
    public static partial void PlatformAccountRoleRemoved(this ILogger logger, StreamingPlatform platform, PlatformAuthRole role, string accountId);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Порт HTTP сервера обновлён с {OldPort} на {NewPort} в соответствии с RedirectUri")]
    public static partial void SettingsHttpPortUpdated(this ILogger logger, int oldPort, int newPort);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "Перезапуск HTTP сервера: порт {OldPort} -> {NewPort}")]
    public static partial void SettingsHttpServerRestarting(this ILogger logger, int oldPort, int newPort);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error, Message = "Ошибка перезапуска HTTP сервера на порту {NewPort}")]
    public static partial void SettingsHttpServerRestartFailed(this ILogger logger, Exception? exception, int newPort);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Information, Message = "Пользователь сохранил настройки")]
    public static partial void SettingsSavedByUser(this ILogger logger);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Error, Message = "Настройки оболочки не записаны на диск: {FilePath}")]
    public static partial void UiSettingsWriteFailed(this ILogger logger, Exception? exception, string filePath);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Error, Message = "Ошибка обновления информации о стриме")]
    public static partial void StreamInfoUpdateFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "Ошибка автообновления информации о стриме")]
    public static partial void StreamInfoAutoUpdateFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "Не удалось положить лог-секцию в буфер обмена")]
    public static partial void SupportClipboardCopyFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Error, Message = "Не удалось открыть форму репорта")]
    public static partial void SupportReportFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1310, Level = LogLevel.Error, Message = "Не удалось сохранить раскладку панели, правки останутся несохранёнными")]
    public static partial void DashboardLayoutSaveFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1311, Level = LogLevel.Debug, Message = "Пропорции узла {NodePath} панели не изменены: доли {Shares} отклонены")]
    public static partial void DashboardResizeRefused(this ILogger logger, string nodePath, string shares);

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

    [LoggerMessage(EventId = 1409, Level = LogLevel.Warning, Message = "Не удалось управлять дополнительным выходом OBS «{OutputName}»")]
    public static partial void ObsSecondaryOutputActionFailed(this ILogger logger, Exception? exception, string outputName);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Information, Message = "Пользователь открыл перенос данных предыдущей версии из настроек: источников – {Count}")]
    public static partial void LegacyImportOpenedFromSettings(this ILogger logger, int count);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Warning, Message = "Перенос данных предыдущей версии недоступен в автоматическом режиме")]
    public static partial void LegacyImportSuppressedHeadless(this ILogger logger);

    [LoggerMessage(EventId = 1330, Level = LogLevel.Warning, Message = "Поиск данных предыдущей версии не завершился")]
    public static partial void LegacyImportSourceSearchFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1331, Level = LogLevel.Error, Message = "Перенос данных предыдущей версии прервался")]
    public static partial void LegacyImportInterrupted(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1332, Level = LogLevel.Information, Message = "Поиск данных предыдущей версии занял {ElapsedMs:F0} мс")]
    public static partial void LegacyImportSearchCompleted(this ILogger logger, double elapsedMs);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Information, Message = "Пользователь подставил недавний канал отладки {Channel}")]
    public static partial void RecentDebugChannelChosen(this ILogger logger, string channel);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Пользователь убрал канал {Channel} из недавних каналов отладки")]
    public static partial void RecentDebugChannelRemoved(this ILogger logger, string channel);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Error, Message = "Не удалось убрать канал {Channel} из недавних каналов отладки")]
    public static partial void RecentDebugChannelRemoveFailed(this ILogger logger, Exception? exception, string channel);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Warning, Message = "Проверка состояния недавних каналов отладки сорвалась")]
    public static partial void RecentDebugChannelsStatusFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "Обновление {Version} загружено, приложение перезапустится")]
    public static partial void UpdateDownloaded(this ILogger logger, Version version);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Error, Message = "Ошибка загрузки обновления")]
    public static partial void UpdateDownloadFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information, Message = "Версия {Version} пропущена")]
    public static partial void UpdateVersionSkipped(this ILogger logger, Version version);

    [LoggerMessage(EventId = 1320, Level = LogLevel.Warning, Message = "Не удалось получить обложки игр, категории останутся без картинок")]
    public static partial void BoxArtLoadFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1321, Level = LogLevel.Debug, Message = "Обложка игры не прочитана из кеша: {Path}")]
    public static partial void BoxArtDecodeFailed(this ILogger logger, Exception? exception, string path);

    [LoggerMessage(EventId = 1322, Level = LogLevel.Debug, Message = "Кадр превью стрима не загружен: {Address}")]
    public static partial void StreamPreviewFrameFailed(this ILogger logger, Exception? exception, string address);

    public static void StreamPreviewFrameFailed(this ILogger logger, Exception? exception, Uri address)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.StreamPreviewFrameFailed(exception, address.GetLeftPart(UriPartial.Path));
        }
    }

    [LoggerMessage(EventId = 1323, Level = LogLevel.Warning, Message = "Не удалось получить аватарки пользователей, вместо них останутся буквы ников")]
    public static partial void UserAvatarLoadFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1324, Level = LogLevel.Debug, Message = "Аватарка пользователя не прочитана из кеша: {Path}")]
    public static partial void UserAvatarDecodeFailed(this ILogger logger, Exception? exception, string path);

    [LoggerMessage(EventId = 1325, Level = LogLevel.Debug, Message = "Картинка из сообщения не загружена: {Address}")]
    public static partial void ChatMessageImageLoadFailed(this ILogger logger, Exception? exception, string address);

    public static void ChatMessageImageLoadFailed(this ILogger logger, Exception? exception, Uri address)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.ChatMessageImageLoadFailed(exception, address.GetLeftPart(UriPartial.Path));
        }
    }

    [LoggerMessage(EventId = 1326, Level = LogLevel.Debug, Message = "Картинка из сообщения не декодирована: {Address}")]
    public static partial void ChatMessageImageDecodeFailed(this ILogger logger, Exception? exception, string address);

    public static void ChatMessageImageDecodeFailed(this ILogger logger, Exception? exception, Uri address)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.ChatMessageImageDecodeFailed(exception, address.GetLeftPart(UriPartial.Path));
        }
    }

    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "Команда {Command} {State} на странице «Команды»")]
    public static partial void CommandEnabledChanged(this ILogger logger, string command, string state);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Information, Message = "Цель ответа команды {Command} на странице «Команды»: {Target}")]
    public static partial void CommandTargetChanged(this ILogger logger, string command, string target);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Information, Message = "Общая цель ответа команд на странице «Команды»: {Target}")]
    public static partial void CommandDefaultTargetChanged(this ILogger logger, string target);

    [LoggerMessage(EventId = 1603, Level = LogLevel.Error, Message = "Не удалось сохранить настройки команд, правка отменена")]
    public static partial void CommandSettingsSaveFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1604, Level = LogLevel.Information, Message = "Права на команду {Command} на странице «Команды»: {Access}")]
    public static partial void CommandAccessChanged(this ILogger logger, string command, string access);

    [LoggerMessage(EventId = 1605, Level = LogLevel.Information, Message = "Параметры команды {Command} сохранены со страницы «Команды»")]
    public static partial void CommandParametersSaved(this ILogger logger, string command);

    [LoggerMessage(EventId = 1606, Level = LogLevel.Error, Message = "Не удалось сохранить параметры команды {Command}")]
    public static partial void CommandParametersSaveFailed(this ILogger logger, Exception? exception, string command);

    [LoggerMessage(EventId = 1607, Level = LogLevel.Warning, Message = "Параметры команды {Command} приняты в памяти: файл настроек не переписан")]
    public static partial void CommandParametersNotWritten(this ILogger logger, string command);

    [LoggerMessage(EventId = 1700, Level = LogLevel.Information, Message = "Пользователь переключил активный канал {Platform} на {Channel}")]
    public static partial void ChannelSwitchedByUser(this ILogger logger, string platform, string channel);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Information, Message = "Пользователь выбрал следующий канал {Platform}: {Channel}")]
    public static partial void ChannelPreselectedByUser(this ILogger logger, string platform, string channel);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Information, Message = "Пользователь подтвердил переключение по автопоиску {Platform} -> {Channel}")]
    public static partial void AutoDiscoverConfirmedByUser(this ILogger logger, string platform, string channel);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Information, Message = "Пользователь отклонил переключение по автопоиску {Platform}")]
    public static partial void AutoDiscoverDismissedByUser(this ILogger logger, string platform);

    [LoggerMessage(EventId = 1704, Level = LogLevel.Error, Message = "Сбой переключения канала {Platform}")]
    public static partial void ChannelSwitchOperationFailed(this ILogger logger, Exception? exception, string platform);

    [LoggerMessage(EventId = 1800, Level = LogLevel.Warning, Message = "Не удалось получить текущие параметры канала для редактирования профиля")]
    public static partial void BroadcastProfileChannelFetchFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Warning, Message = "Не удалось применить профиль трансляции")]
    public static partial void BroadcastProfileApplyFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1802, Level = LogLevel.Warning, Message = "Не удалось применить профиль «{ProfileName}» после изменения номера серии")]
    public static partial void BroadcastProfileNumberAdjustFailed(this ILogger logger, Exception? exception, string profileName);

    [LoggerMessage(EventId = 1803, Level = LogLevel.Warning, Message = "Профиль трансляции отклонён при сохранении")]
    public static partial void BroadcastProfileSaveRejected(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1804, Level = LogLevel.Warning, Message = "Не удалось завершить голосование")]
    public static partial void PollEndFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1805, Level = LogLevel.Warning, Message = "Не удалось запустить голосование по профилю «{ProfileName}»")]
    public static partial void PollStartFailed(this ILogger logger, Exception? exception, string profileName);

    [LoggerMessage(EventId = 1806, Level = LogLevel.Error, Message = "Не удалось изменить баланс пользователя")]
    public static partial void UserBalanceAdjustmentFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1807, Level = LogLevel.Warning, Message = "Не удалось подготовить папку с настройками: {Path}")]
    public static partial void SettingsFolderPrepareFailed(this ILogger logger, Exception? exception, string path);

    [LoggerMessage(EventId = 1808, Level = LogLevel.Error, Message = "Ошибка сброса настроек к значениям по умолчанию")]
    public static partial void SettingsResetFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1809, Level = LogLevel.Warning, Message = "Не удалось прочитать файл импорта профилей: {Path}")]
    public static partial void BroadcastProfilesImportReadFailed(this ILogger logger, Exception? exception, string path);

    [LoggerMessage(EventId = 1810, Level = LogLevel.Warning, Message = "Ошибка поиска категорий игр в автодополнении")]
    public static partial void GameAutocompleteSearchFailed(this ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1811, Level = LogLevel.Information, Message = "Перенос баллов со страницы «Пользователи»: перенесено {TransferredCount}, пропущено {SkippedCount}, баллов {TotalPoints}")]
    public static partial void UserPointsTransferred(this ILogger logger, int transferredCount, int skippedCount, long totalPoints);

    [LoggerMessage(EventId = 1812, Level = LogLevel.Error, Message = "Ошибка переноса баллов между источниками")]
    public static partial void UserPointsTransferFailed(this ILogger logger, Exception? exception);
}
