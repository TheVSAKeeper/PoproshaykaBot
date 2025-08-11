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

        UnifiedHttpServer? httpServer = null;

        var twitchSettings = settingsManager.Current.Twitch;

        var portValidator = new PortValidator();

        if (twitchSettings.HttpServerEnabled)
        {
            if (portValidator.ValidateAndResolvePortConflict(settingsManager.Current, settingsManager))
            {
                httpServer = new(chatHistoryManager, settingsManager, twitchSettings.HttpServerPort);

                try
                {
                    httpServer.StartAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка запуска HTTP сервера: {ex.Message}");
                    httpServer = null;
                }
            }
            else
            {
                Console.WriteLine("Не удалось разрешить конфликт портов. HTTP сервер не запущен.");
            }
        }

        var twitchApi = new TwitchAPI();
        var eventSubClient = new EventSubWebsocketClient();
        var streamStatusManager = new StreamStatusManager(eventSubClient, twitchApi);

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

            var bot = new Bot(accessToken,
                settingsManager.Current.Twitch,
                statistics,
                twitchClient,
                twitchApi,
                streamStatusManager);

            return bot;
        }

        async Task<string?> GetAccessTokenAsync()
        {
            var settings = settingsManager.Current.Twitch;

            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                Console.WriteLine("OAuth настройки не настроены (ClientId/ClientSecret).");
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
                        Console.WriteLine($"Не удалось обновить токен: {ex.Message}");
                    }
                }
            }

            try
            {
                var accessToken = await oauthService.StartOAuthFlowAsync(settings.ClientId,
                    settings.ClientSecret,
                    httpServer,
                    settings.Scopes,
                    settings.RedirectUri);

                Console.WriteLine("OAuth авторизация завершена успешно.");
                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OAuth авторизация не удалась: {ex.Message}");
                return null;
            }
        }

        var connectionManager = new BotConnectionManager(BotFactory, GetAccessTokenAsync);

        using var mainForm = new MainForm(statistics, chatHistoryManager, httpServer, connectionManager, settingsManager, oauthService);
        Application.Run(mainForm);
    }
}
