using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Services;
using PoproshaykaBot.WinForms.Services.Http;
using PoproshaykaBot.WinForms.Services.Http.Handlers;
using PoproshaykaBot.WinForms.Settings;
using Pure.DI;
using TwitchLib.Api;
using TwitchLib.EventSub.Websockets;
using static Pure.DI.Lifetime;

namespace PoproshaykaBot.WinForms;

public partial class Composition
{
    private static void Setup()
    {
        DI.Setup("Composition")
            .Bind<SettingsManager>().As(Singleton).To<SettingsManager>()
            .Bind<TwitchAPI>().As(Singleton).To<TwitchAPI>()
            .Bind<StatisticsCollector>().As(Singleton).To<StatisticsCollector>()
            .Bind<TwitchOAuthService>().As(Singleton).To<TwitchOAuthService>()
            .Bind<ChatHistoryManager>().As(Singleton).To<ChatHistoryManager>()
            .Bind<PortValidator>().To<PortValidator>()
            .Bind<SseService>().As(Singleton).To<SseService>()
            .Bind<OAuthHandler>().As(Singleton).To<OAuthHandler>()
            .Bind<OverlayHandler>().As(Singleton).To<OverlayHandler>()
            .Bind<SseHandler>().As(Singleton).To<SseHandler>()
            .Bind<ApiHistoryHandler>().As(Singleton).To<ApiHistoryHandler>()
            .Bind<ApiChatSettingsHandler>().As(Singleton).To<ApiChatSettingsHandler>()
            .Bind<Router>().As(Singleton).To(ctx =>
            {
                ctx.Inject(out OAuthHandler oauth);
                ctx.Inject(out OverlayHandler overlay);
                ctx.Inject(out SseHandler sse);
                ctx.Inject(out ApiHistoryHandler apiHistory);
                ctx.Inject(out ApiChatSettingsHandler apiSettings);

                var router = new Router();
                router.Register("/", oauth);
                router.Register("/chat", overlay);
                router.Register("/events", sse);
                router.Register("/api/history", apiHistory);
                router.Register("/api/chat-settings", apiSettings);

                router.Register("/assets/obs.css", new StaticContentHandler("PoproshaykaBot.WinForms.Assets.obs.css", "text/css; charset=utf-8"));
                router.Register("/assets/obs.js", new StaticContentHandler("PoproshaykaBot.WinForms.Assets.obs.js", "application/javascript; charset=utf-8"));
                router.Register("/favicon.ico", new StaticContentHandler("PoproshaykaBot.WinForms.icon.ico", "image/x-icon"));

                return router;
            })
            .Bind<UnifiedHttpServer>().As(Singleton).To(ctx =>
            {
                ctx.Inject(out SettingsManager settings);
                ctx.Inject(out ChatHistoryManager history);
                ctx.Inject(out Router router);
                ctx.Inject(out SseService sseService);

                return new UnifiedHttpServer(history, router, sseService, settings.Current.Twitch.HttpServerPort);
            })
            .Bind<EventSubWebsocketClient>().As(Singleton).To<EventSubWebsocketClient>()
            .Bind<StreamStatusManager>().As(Singleton).To<StreamStatusManager>()
            .Bind<ChatDecorationsProvider>().As(Singleton).To<ChatDecorationsProvider>()
            .Bind<UserRankService>().As(Singleton).To<UserRankService>()
            .Bind<TokenService>().As(Singleton).To<TokenService>()
            .Bind<BotFactory>().As(Singleton).To<BotFactory>()
            .Bind<Func<string, Bot>>().To(ctx =>
            {
                ctx.Inject(out BotFactory factory);
                return (Func<string, Bot>)factory.Create;
            })
            .Bind<BotConnectionManager>().To<BotConnectionManager>()
            .Bind<MainForm>().To<MainForm>()
            .Root<MainForm>("MainForm")
            .Root<SettingsManager>("SettingsManager")
            .Root<StatisticsCollector>("StatisticsCollector")
            .Root<PortValidator>("PortValidator")
            .Root<UnifiedHttpServer>("UnifiedHttpServer");
    }
}
