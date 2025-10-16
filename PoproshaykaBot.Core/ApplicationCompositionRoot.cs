using PoproshaykaBot.Core.Application.Bot;
using PoproshaykaBot.Core.Application.Broadcasting;
using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Application.Chat.Commands;
using PoproshaykaBot.Core.Application.Chat.Commands.Implementations;
using PoproshaykaBot.Core.Application.Statistics;
using PoproshaykaBot.Core.Application.Streaming;
using PoproshaykaBot.Core.Infrastructure.ExternalServices.Twitch;
using PoproshaykaBot.Core.Infrastructure.Http;
using PoproshaykaBot.Core.Infrastructure.Http.Server;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;

namespace PoproshaykaBot.Core;

/// <summary>
/// Главный Composition Root для всего приложения.
/// Инкапсулирует создание объектного графа всех компонентов приложения (Pure DI).
/// </summary>
public class ApplicationCompositionRoot
{
    private readonly TwitchAPI _twitchApi;
    private readonly EventSubWebsocketClient _eventSubClient;
    private readonly StreamStatusManager _streamStatusManager;
    private readonly ChatDecorationsProvider _chatDecorationsProvider;

    public ApplicationCompositionRoot()
    {
        SettingsManager = new();
        StatisticsCollector = new();
        OAuthService = new(SettingsManager);
        ChatHistoryManager = new(SettingsManager);

        var resolvedPort = SettingsManager.Current.Twitch.HttpServerPort;
        HttpServer = HttpServerCompositionRoot.CreateHttpServer(ChatHistoryManager, SettingsManager, resolvedPort);

        _twitchApi = new();
        _eventSubClient = new();
        _streamStatusManager = new(_eventSubClient, _twitchApi);
        _chatDecorationsProvider = new(_twitchApi);

        ConnectionManager = new(CreateBot, GetAccessTokenAsync);
    }

    /// <summary>
    /// SettingsManager - управление настройками.
    /// </summary>
    public SettingsManager SettingsManager { get; }

    /// <summary>
    /// StatisticsCollector - сбор статистики.
    /// </summary>
    public StatisticsCollector StatisticsCollector { get; }

    /// <summary>
    /// TwitchOAuthService - OAuth авторизация.
    /// </summary>
    public TwitchOAuthService OAuthService { get; }

    /// <summary>
    /// ChatHistoryManager - управление историей чата.
    /// </summary>
    public ChatHistoryManager ChatHistoryManager { get; }

    /// <summary>
    /// UnifiedHttpServer - HTTP сервер для OAuth, SSE, API.
    /// </summary>
    public UnifiedHttpServer HttpServer { get; }

    /// <summary>
    /// BotConnectionManager - управление подключением бота.
    /// </summary>
    public BotConnectionManager ConnectionManager { get; }

    /// <summary>
    /// Фабрика создания бота с полной конфигурацией зависимостей.
    /// </summary>
    private Bot CreateBot(string accessToken)
    {
        var settings = SettingsManager.Current.Twitch;
        var credentials = new ConnectionCredentials(settings.BotUsername, accessToken);

        var clientOptions = new ClientOptions
        {
            MessagesAllowedInPeriod = settings.MessagesAllowedInPeriod,
            ThrottlingPeriod = TimeSpan.FromSeconds(settings.ThrottlingPeriodSeconds),
        };

        var wsClient = new WebSocketClient(clientOptions);
        var twitchClient = new TwitchClient(wsClient);
        twitchClient.Initialize(credentials, settings.Channel);

        _twitchApi.Settings.ClientId = settings.ClientId;
        _twitchApi.Settings.AccessToken = accessToken;

        var messageProvider = new Func<int, string>(counter =>
        {
            var template = settings.AutoBroadcast.BroadcastMessageTemplate;
            var info = _streamStatusManager.CurrentStream;

            return template
                .Replace("{counter}", counter.ToString())
                .Replace("{title}", info?.Title ?? string.Empty)
                .Replace("{game}", info?.GameName ?? string.Empty)
                .Replace("{viewers}", info?.ViewerCount.ToString() ?? string.Empty);
        });

        var messenger = new TwitchChatMessenger(twitchClient, SettingsManager);
        messenger.MessageSent += ChatHistoryManager.AddMessage;

        var broadcastScheduler = new BroadcastScheduler(messenger, SettingsManager, messageProvider);
        var audienceTracker = new AudienceTracker(SettingsManager);

        var commands = new List<IChatCommand>
        {
            new HelloCommand(),
            new DonateCommand(),
            new HowManyMessagesCommand(StatisticsCollector),
            new BotStatsCommand(StatisticsCollector),
            new TopUsersCommand(StatisticsCollector),
            new MyProfileCommand(StatisticsCollector),
            new ActiveUsersCommand(audienceTracker),
            new ByeCommand(audienceTracker),
            new StreamInfoCommand(_streamStatusManager),
            new TrumpCommand(SettingsManager),
        };

        var commandProcessor = new ChatCommandProcessor(commands);
        commandProcessor.Register(new HelpCommand(commandProcessor.GetAllCommands));

        var messageHandler = new BotMessageHandler(StatisticsCollector,
            audienceTracker,
            ChatHistoryManager,
            _chatDecorationsProvider,
            commandProcessor,
            messenger,
            settings);

        var lifecycleManager = new BotLifecycleManager(twitchClient,
            StatisticsCollector,
            _chatDecorationsProvider,
            _streamStatusManager,
            settings,
            _twitchApi);

        var bot = new Bot(settings,
            twitchClient,
            messenger,
            _twitchApi,
            audienceTracker,
            broadcastScheduler,
            _streamStatusManager,
            lifecycleManager,
            messageHandler);

        return bot;
    }

    /// <summary>
    /// Получение валидного access token через OAuth flow.
    /// </summary>
    private async Task<string?> GetAccessTokenAsync()
    {
        var settings = SettingsManager.Current.Twitch;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("OAuth настройки не настроены (ClientId/ClientSecret).");
        }

        if (!string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            if (await OAuthService.IsTokenValidAsync(settings.AccessToken))
            {
                Console.WriteLine("Используется сохранённый токен доступа.");
                return settings.AccessToken;
            }

            if (!string.IsNullOrWhiteSpace(settings.RefreshToken))
            {
                try
                {
                    Console.WriteLine("Обновление токена доступа...");
                    var validToken = await OAuthService.GetValidTokenAsync(settings.ClientId,
                        settings.ClientSecret,
                        settings.AccessToken,
                        settings.RefreshToken);

                    Console.WriteLine("Токен доступа обновлён.");
                    return validToken;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Не удалось обновить токен доступа: {ex.Message}", ex);
                }
            }
        }

        try
        {
            var accessToken = await OAuthService.StartOAuthFlowAsync(settings.ClientId,
                settings.ClientSecret,
                HttpServer.IsRunning ? HttpServer : null,
                settings.Scopes,
                settings.RedirectUri);

            Console.WriteLine("OAuth авторизация завершена успешно.");
            return accessToken;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OAuth авторизация не удалась: {ex.Message}", ex);
        }
    }
}
