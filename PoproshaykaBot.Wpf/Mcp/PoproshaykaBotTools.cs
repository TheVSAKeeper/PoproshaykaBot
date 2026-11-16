using KeepShell.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PoproshaykaBot.Core.Platforms;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.Mcp;

[McpServerToolType]
public sealed class PoproshaykaBotTools
{
    private PoproshaykaBotTools()
    {
    }

    [McpServerTool(Name = "get_bot_state")]
    [Description("Доменное состояние PoproshaykaBot: версия сборки, фаза подключения бота, целевой канал (свой или отладочный, разрешена ли отправка), статус стрима с названием, категорией, зрителями и временем начала, HTTP-сервер оверлея с портом и адресом, подключение к OBS, а также срезы по площадкам и состоянию эфира. Звать первым – по этому ответу видно, работает бот или стоит, и есть ли смысл дальше ходить по экранам. Данные читаются в обход окна, поэтому ответ не зависит от открытой страницы.")]
    public static string GetBotState(BotAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetState());
    }

    [McpServerTool(Name = "connect_bot")]
    [Description("Подключает бота к чату (ко всем включённым площадкам или к указанной в параметре platform: Twitch или YouTube) – тот же путь, что кнопка подключения в оболочке, без диалогов подтверждения. Подключение идёт в фоне: ответ несёт фазу сразу после запроса, дождаться Connected можно повторным get_bot_state. Отказывает, если бот уже подключается, подключён или отключается.")]
    public static string ConnectBot(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "connect_bot");
        var parsedPlatform = ParseOptionalPlatform(platform);

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
    [Description("Отключает бота от чата (от всех площадок или от указанной в параметре platform: Twitch или YouTube) – тот же путь, что кнопка отключения в оболочке, без диалогов подтверждения. Ждёт завершения отключения и возвращает фазу после него; идущее подключение отменяет и ждёт отмены (фаза Cancelled). Отказывает, если бот не подключён.")]
    public static async Task<string> DisconnectBotAsync(
        BotAutomation automation,
        McpPreferences preferences,
        [Description("Необязательная площадка: Twitch или YouTube")] string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        McpGuards.RequireMutations(preferences, "disconnect_bot");
        var parsedPlatform = ParseOptionalPlatform(platform);

        try
        {
            return McpFormat.Serialize(new { accepted = true, phase = await automation.DisconnectAsync(parsedPlatform) });
        }
        catch (InvalidOperationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    private static StreamingPlatform? ParseOptionalPlatform(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<StreamingPlatform>(raw.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new McpException($"Неизвестная площадка «{raw}». Допустимые значения: Twitch, YouTube.");
    }
}
