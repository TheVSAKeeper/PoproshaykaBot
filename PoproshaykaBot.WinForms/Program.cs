using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Settings;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;

namespace PoproshaykaBot.WinForms;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settingsManager = new SettingsManager();
        var statistics = new StatisticsCollector();
        var oauthService = new TwitchOAuthService(settingsManager);
        var chatHistoryManager = new ChatHistoryManager();
        var twitchSettings = settingsManager.Current.Twitch;
        var portValidator = new PortValidator(settingsManager);
        var httpServerEnabled = twitchSettings.HttpServerEnabled;
        var portValidationPassed = false;

        if (httpServerEnabled)
        {
            portValidationPassed = portValidator.ValidateAndResolvePortConflict();
        }

        var resolvedPort = settingsManager.Current.Twitch.HttpServerPort;
        var httpServer = new UnifiedHttpServer(chatHistoryManager, settingsManager, resolvedPort);
        var httpServerStarted = false;

        if (httpServerEnabled && portValidationPassed)
        {
            try
            {
                httpServer.StartAsync().GetAwaiter().GetResult();
                httpServerStarted = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else if (httpServerEnabled && portValidationPassed == false)
        {
            MessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        var twitchApi = new TwitchAPI();
        var eventSubClient = new EventSubWebsocketClient();
        var streamStatusManager = new StreamStatusManager(eventSubClient, twitchApi);
        var chatDecorationsProvider = new ChatDecorationsProvider(twitchApi);

        Bot BotFactory(string accessToken)
        {
            var credentials = new ConnectionCredentials(settingsManager.Current.Twitch.BotUsername, accessToken);

            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = settingsManager.Current.Twitch.MessagesAllowedInPeriod,
                ThrottlingPeriod = TimeSpan.FromSeconds(settingsManager.Current.Twitch.ThrottlingPeriodSeconds),
            };

            var wsClient = new WebSocketClient(clientOptions);
            var twitchClient = new TwitchClient(wsClient);
            twitchClient.Initialize(credentials, settingsManager.Current.Twitch.Channel);

            twitchApi.Settings.ClientId = settingsManager.Current.Twitch.ClientId;
            twitchApi.Settings.AccessToken = accessToken;

            var messageProvider = new Func<int, string>(counter =>
            {
                var template = settingsManager.Current.Twitch.AutoBroadcast.BroadcastMessageTemplate;
                var info = streamStatusManager.CurrentStream;

                return template
                    .Replace("{counter}", counter.ToString())
                    .Replace("{title}", info?.Title ?? string.Empty)
                    .Replace("{game}", info?.GameName ?? string.Empty)
                    .Replace("{viewers}", info?.ViewerCount.ToString() ?? string.Empty);
            });

            var broadcastScheduler = new BroadcastScheduler(twitchClient, settingsManager, messageProvider);
            var audienceTracker = new AudienceTracker(settingsManager);

            var commands = new List<IChatCommand>
            {
                new HelloCommand(),
                new DonateCommand(),
                new HowManyMessagesCommand(statistics),
                new BotStatsCommand(statistics),
                new TopUsersCommand(statistics),
                new MyProfileCommand(statistics),
                new ActiveUsersCommand(audienceTracker),
                new ByeCommand(audienceTracker),
                new StreamInfoCommand(streamStatusManager),
                new TrumpCommand(settingsManager),
            };

            var commandProcessor = new ChatCommandProcessor(commands);
            commandProcessor.Register(new HelpCommand(commandProcessor.GetAllCommands));

            var bot = new Bot(settingsManager.Current.Twitch,
                statistics,
                twitchClient,
                twitchApi,
                chatDecorationsProvider,
                audienceTracker,
                broadcastScheduler,
                commandProcessor,
                streamStatusManager);

            return bot;
        }

        async Task<string?> GetAccessTokenAsync()
        {
            var settings = settingsManager.Current.Twitch;

            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                MessageBox.Show("OAuth настройки не настроены (ClientId/ClientSecret).", "Ошибка конфигурации OAuth", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            if (string.IsNullOrWhiteSpace(settings.AccessToken) == false)
            {
                if (await oauthService.IsTokenValidAsync(settings.AccessToken))
                {
                    Console.WriteLine("Используется сохранённый токен доступа.");
                    return settings.AccessToken;
                }

                if (string.IsNullOrWhiteSpace(settings.RefreshToken) == false)
                {
                    try
                    {
                        Console.WriteLine("Обновление токена доступа...");

                        var validToken = await oauthService.GetValidTokenAsync(settings.ClientId,
                            settings.ClientSecret,
                            settings.AccessToken,
                            settings.RefreshToken);

                        Console.WriteLine("Токен доступа обновлён.");
                        return validToken;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Не удалось обновить токен доступа: {ex.Message}", "Ошибка обновления токена", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            try
            {
                var accessToken = await oauthService.StartOAuthFlowAsync(settings.ClientId,
                    settings.ClientSecret,
                    httpServerStarted ? httpServer : null,
                    settings.Scopes,
                    settings.RedirectUri);

                Console.WriteLine("OAuth авторизация завершена успешно.");
                return accessToken;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"OAuth авторизация не удалась: {ex.Message}", "Ошибка OAuth авторизации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        var connectionManager = new BotConnectionManager(BotFactory, GetAccessTokenAsync);
        statistics.LoadStatisticsAsync().GetAwaiter().GetResult();

        // TODO: Изменить время жизни бота
        using var mainForm = new MainForm(chatHistoryManager,
            httpServer,
            connectionManager,
            settingsManager,
            oauthService,
            statistics);

        Application.Run(mainForm);
    }
}
