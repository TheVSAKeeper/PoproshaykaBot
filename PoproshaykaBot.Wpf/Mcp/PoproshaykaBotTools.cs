using KeepShell.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PoproshaykaBot.Core.Platforms;
using System.ComponentModel;
using System.Globalization;

namespace PoproshaykaBot.Wpf.Mcp;

[McpServerToolType]
public sealed class PoproshaykaBotTools
{
    private PoproshaykaBotTools()
    {
    }

    [McpServerTool(Name = "get_bot_state")]
    [Description("Доменное состояние PoproshaykaBot: версия сборки, фаза подключения бота, целевой канал (свой или отладочный, разрешена ли отправка и запись статистики), статус стрима с названием, категорией, зрителями и временем начала, HTTP-сервер оверлея с фактическим/настроенным портом и эндпоинтами, подключение к OBS, а также срезы по площадкам и состоянию эфира. Звать первым – данные читаются в обход окна, поэтому ответ не зависит от открытой страницы.")]
    public static string GetBotState(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetState());
    }

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Полный снимок диагностики PoproshaykaBot без переключения экрана: память процесса и дочерних процессов WebView2, обе сессии Twitch EventSub (статус, попытки переподключения, последнее сообщение), состояние чата, SSE-оверлея, очереди отправки, фоновых задач, мониторинга/транспорта/квоты YouTube и счётчиков шины событий.")]
    public static string GetDiagnostics(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        try
        {
            return McpFormat.Serialize(automation.GetDiagnostics());
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "get_channels_state")]
    [Description("Каталог каналов и состояние мониторинга по площадкам (без токенов и секретов): активный и следующий канал, режим переключения, назначенные и подтверждённые роли Bot/Broadcaster по каждому каналу, актуальные снимки мониторинга (доступность, причина деградации, статус эфира, зрители), группы каналов и активные подсказки автопоиска.")]
    public static string GetChannelsState(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        try
        {
            return McpFormat.Serialize(automation.GetChannelsState());
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "connect_bot")]
    [Description("Подключает бота к чату (ко всем включённым площадкам или к указанной в параметре platform: Twitch или YouTube) – тот же путь, что кнопка подключения в оболочке, без диалогов подтверждения. Подключение идёт в фоне: ответ несёт фазу сразу после запроса, дождаться Connected можно повторным get_bot_state. Отказывает, если открыт мастер настройки или подключать нечего.")]
    public static string ConnectBot(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "connect_bot");
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(new { accepted = true, phase = automation.Connect(parsedPlatform) });
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "disconnect_bot")]
    [Description("Отключает бота от чата (от всех площадок или от указанной в параметре platform: Twitch или YouTube) – тот же путь, что кнопка отключения в оболочке, без диалогов подтверждения. Ждёт завершения отключения и возвращает фазу после него; идущее подключение отменяет и ждёт отмены (фаза Cancelled). Отказывает, если открыт мастер настройки или бот не подключён.")]
    public static async Task<string> DisconnectBotAsync(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "disconnect_bot");
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(new
            {
                accepted = true,
                phase = await automation.DisconnectAsync(parsedPlatform, cancellationToken),
            });
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "get_runtime_health")]
    [Description("Сводка состояния рантайма и хранилищ: версия и отметка сборки, режим хранения (portable/AppData/override), признак нештатного прошлого запуска, необработанные фоновые задачи, ошибки привязок WPF, статус файлов отображения чата, состояние загрузки статистики и журналов эфиров/голосований, статус автообновления, сводка раскладки дашборда, версия WebView2 Runtime и список мониторов с DPI.")]
    public static string GetRuntimeHealth(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetRuntimeHealth());
    }

    [McpServerTool(Name = "get_settings_summary")]
    [Description("Обезличенная сводка всех секций настроек через ISettingsSnapshotContributor (без секретов, паролей и токенов).")]
    public static string GetSettingsSummary(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetSettingsSummary());
    }

    [McpServerTool(Name = "get_accounts_readiness")]
    [Description("Готовность онбординга и всех авторизаций по площадкам без утечки секретов и токенов: чеклист мастера, список аккаунтов и контекстов ролей Bot/Broadcaster, наличие и срок действия токенов, выданные и недостающие scopes, наличие браузерного профиля чата и опциональная проверка сохранённости на диске.")]
    public static string GetAccountsReadiness(
        BotAutomation automation,
        [Description("Проверить, что авторизация реально подтверждена на диске")] bool verifyPersisted = false)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetAccountsReadiness(verifyPersisted));
    }

    [McpServerTool(Name = "get_chat_history")]
    [Description("Последние сообщения из локального буфера ChatHistoryManager (только чтение, ничего не отправляет в чат): активные области истории и отфильтрованные сообщения с метками командных ответов и типом.")]
    public static string GetChatHistory(
        BotAutomation automation,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null,
        [Description("Необязательный фильтр по имени или ID канала")] string? channel = null,
        [Description("Только активные каналы (по умолчанию true)")] bool activeOnly = true,
        [Description("Максимум сообщений в ответе (1..200, по умолчанию 50)")] int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(automation);
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(automation.GetChatHistory(parsedPlatform, channel, activeOnly, limit));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "get_commands_state")]
    [Description("Состояние всех зарегистрированных чат-команд: префикс, цель ответа по умолчанию, алиасы, требуемые возможности площадки, включённость, уровень доступа, индивидуальная цель ответа, доступность на указанной площадке, счётчики вызовов и число ожидающих эхо-ответов.")]
    public static string GetCommandsState(
        BotAutomation automation,
        [Description("Необязательная площадка для проверки включённости и доступности: Twitch или YouTube")] string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(automation.GetCommandsState(parsedPlatform));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "get_broadcasts_state")]
    [Description("Состояние эфиров, профилей трансляций и планировщика авто-рассылки: активный черновик эфира, состояние хранилища и последние записи истории эфиров, настроенные профили (название, номер серии, сцена OBS, целевые площадки) и статус планировщика.")]
    public static string GetBroadcastsState(
        BotAutomation automation,
        [Description("Количество последних эфиров в истории (1..50, по умолчанию 10)")] int recentLimit = 10)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetBroadcastsState(recentLimit));
    }

    [McpServerTool(Name = "get_polls_state")]
    [Description("Состояние голосований Twitch: активный снимок опроса, доступность Polls API на канале, число настроенных профилей опросов, здоровье хранилища истории и последние завершённые опросы.")]
    public static async Task<string> GetPollsStateAsync(
        BotAutomation automation,
        [Description("Количество последних опросов в истории (1..50, по умолчанию 10)")] int recentLimit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(await automation.GetPollsStateAsync(recentLimit, cancellationToken));
    }

    [McpServerTool(Name = "get_statistics_summary")]
    [Description("Сводка статистики бота и зрителей: состояние загрузки каталога, счётчики BotStatistics, размеры каталога и связок, топ зрителей в выбранном срезе или поиск конкретного зрителя по имени/ID с привязанными аккаунтами и разбивкой по каналам.")]
    public static string GetStatisticsSummary(
        BotAutomation automation,
        [Description("Необязательная площадка среза: Twitch или YouTube")] string? platform = null,
        [Description("Необязательный ID канала (требует указания platform)")] string? channelId = null,
        [Description("Размер топа зрителей (1..50, по умолчанию 10)")] int topCount = 10,
        [Description("Поиск конкретного зрителя по имени или ID вместо топа")] string? queryUser = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(automation.GetStatisticsSummary(parsedPlatform, channelId, topCount, queryUser));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "get_obs_state")]
    [Description("Состояние OBS-интеграции без пароля: включена ли интеграция, хост и порт, настроенные имена сцен и чат-источников, текущий статус подключения и (если OBS уже подключён, без автоподключения) снимок дашборда OBS, плюс опциональные списки сцен и Browser Source.")]
    public static async Task<string> GetObsStateAsync(
        BotAutomation automation,
        [Description("Если OBS подключён, также запросить списки сцен и Browser Source")] bool includeLists = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(await automation.GetObsStateAsync(includeLists, cancellationToken));
    }

    [McpServerTool(Name = "dry_run_command")]
    [Description("Локальная проверка разбора, прав и доступности чат-команды без выполнения, без побочных эффектов и без отправки сообщений в чат.")]
    public static string DryRunCommand(
        BotAutomation automation,
        [Description("Текст сообщения с префиксом команды, например !помощь")] string text,
        [Description("Площадка: Twitch или YouTube (по умолчанию Twitch)")] string? platform = "Twitch",
        [Description("Моделировать права стримера")] bool isBroadcaster = false,
        [Description("Моделировать права модератора")] bool isModerator = false)
    {
        ArgumentNullException.ThrowIfNull(automation);
        var effectivePlatform = string.IsNullOrWhiteSpace(platform) ? "Twitch" : platform;
        var parsedPlatform = ParseRequiredPlatform(effectivePlatform, automation);

        try
        {
            return McpFormat.Serialize(automation.DryRunCommand(text, parsedPlatform, isBroadcaster, isModerator));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "refresh_channel_monitoring")]
    [Description("Принудительно обновляет снимки мониторинга каналов (по всем площадкам, одной площадке или конкретному каналу). Требует включённых мутаций MCP.")]
    public static async Task<string> RefreshChannelMonitoringAsync(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null,
        [Description("Необязательный ID канала (требует указания platform)")] string? channelId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "refresh_channel_monitoring");
        var parsedPlatform = ParseOptionalPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(await automation.RefreshChannelMonitoringAsync(parsedPlatform, channelId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "switch_active_channel")]
    [Description("Переключает активный канал для указанной площадки через IChannelSwitchCoordinator. Требует включённых мутаций MCP.")]
    public static async Task<string> SwitchActiveChannelAsync(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Площадка: Twitch или YouTube")] string platform,
        [Description("Идентификатор целевого канала из каталога")] string channelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "switch_active_channel");
        var parsedPlatform = ParseRequiredPlatform(platform, automation);

        try
        {
            return McpFormat.Serialize(await automation.SwitchActiveChannelAsync(parsedPlatform, channelId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "refresh_obs_chat_sources")]
    [Description("Выполняет жёсткое обновление (refreshnocache) настроенных Browser Source чата в OBS. Требует включённых мутаций MCP.")]
    public static async Task<string> RefreshObsChatSourcesAsync(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Подключиться к OBS автоматически, если соединение сейчас не активно")] bool connectIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "refresh_obs_chat_sources");

        try
        {
            return McpFormat.Serialize(await automation.RefreshObsChatSourcesAsync(connectIfNeeded, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "flush_statistics")]
    [Description("Немедленно сохраняет накопленную статистику и историю на диск через StatisticsAutoSaver.SaveNowAsync. Требует включённых мутаций MCP.")]
    public static async Task<string> FlushStatisticsAsync(
        BotAutomation automation,
        McpPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "flush_statistics");

        try
        {
            return McpFormat.Serialize(await automation.FlushStatisticsAsync(cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "export_diagnostics_bundle")]
    [Description("Собирает и сохраняет вымаранный от секретов ZIP-пакет диагностики в подпапку diagnostics каталога данных через DiagnosticsCollector. Возвращает путь к архиву, число записей и признак полноты вымарывания. Требует включённых мутаций MCP.")]
    public static async Task<string> ExportDiagnosticsBundleAsync(
        BotAutomation automation,
        McpPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "export_diagnostics_bundle");

        try
        {
            return McpFormat.Serialize(await automation.ExportDiagnosticsBundleAsync(cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    private static StreamingPlatform ParseRequiredPlatform(string? raw, BotAutomation automation)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new McpException("Не указана площадка. Допустимые значения: Twitch, YouTube.");
        }

        return ParseOptionalPlatform(raw, automation)
            ?? throw new McpException("Не указана площадка. Допустимые значения: Twitch, YouTube.");
    }

    private static StreamingPlatform? ParseOptionalPlatform(string? raw, BotAutomation automation)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();

        foreach (var candidate in Enum.GetValues<StreamingPlatform>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase)
                && automation.IsSupportedPlatform(candidate))
            {
                return candidate;
            }
        }

        throw new McpException($"Неизвестная площадка «{raw}». Допустимые значения: Twitch, YouTube.");
    }
}
