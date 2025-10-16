using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Infrastructure.Http.Handlers;
using PoproshaykaBot.Core.Infrastructure.Http.Services;
using PoproshaykaBot.Core.Infrastructure.Persistence;

namespace PoproshaykaBot.Core.Infrastructure.Http.Server;

/// <summary>
/// Composition Root для создания и конфигурирования компонентов HTTP сервера.
/// Инкапсулирует логику создания объектного графа для Pure DI.
/// </summary>
public static class HttpServerCompositionRoot
{
    /// <summary>
    /// Создаёт полностью сконфигурированный экземпляр UnifiedHttpServer со всеми зависимостями.
    /// </summary>
    public static UnifiedHttpServer CreateHttpServer(
        ChatHistoryManager chatHistoryManager,
        SettingsManager settingsManager,
        int port = 8080)
    {
        var sseService = new ServerSentEventsService();
        var router = new HttpRequestRouter();
        var oauthHandler = new OAuthCallbackHandler();

        router.RegisterHandler(oauthHandler);
        router.RegisterHandler(new StaticFilesHandler());
        router.RegisterHandler(new ChatApiHandler(chatHistoryManager, settingsManager));
        router.RegisterHandler(new ServerSentEventsHandler(sseService));

        return new(chatHistoryManager,
            settingsManager,
            router,
            sseService,
            oauthHandler,
            port);
    }
}
