using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Settings;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace PoproshaykaBot.WinForms.Services;

public sealed class BotFactory(
    SettingsManager settingsManager,
    StatisticsCollector statistics,
    TwitchAPI twitchApi,
    ChatDecorationsProvider chatDecorationsProvider,
    ChatHistoryManager chatHistoryManager,
    StreamStatusManager streamStatusManager)
{
    public Bot Create(string accessToken)
    {
        var settings = settingsManager.Current.Twitch;
        var credentials = new ConnectionCredentials(settings.BotUsername, accessToken);

        var clientOptions = new ClientOptions
        {
            MessagesAllowedInPeriod = settings.MessagesAllowedInPeriod,
            ThrottlingPeriod = TimeSpan.FromSeconds(settings.ThrottlingPeriodSeconds),
        };

        var wsClient = new WebSocketClient(clientOptions);
        var twitchClient = new TwitchClient(wsClient);
        twitchClient.Initialize(credentials, settings.Channel);

        // TODO: Переделать
        twitchApi.Settings.ClientId = settings.ClientId;
        twitchApi.Settings.AccessToken = accessToken;

        var messenger = new TwitchChatMessenger(twitchClient, settingsManager);
        messenger.MessageSent += chatHistoryManager.AddMessage;

        var messageProvider = new Func<int, string>(counter =>
        {
            var template = settings.AutoBroadcast.BroadcastMessageTemplate;
            var info = streamStatusManager.CurrentStream;

            return template
                .Replace("{counter}", counter.ToString())
                .Replace("{title}", info?.Title ?? string.Empty)
                .Replace("{game}", info?.GameName ?? string.Empty)
                .Replace("{viewers}", info?.ViewerCount.ToString() ?? string.Empty);
        });

        var broadcastScheduler = new BroadcastScheduler(messenger, settingsManager, messageProvider);
        var audienceTracker = new AudienceTracker(settingsManager);
        var userMessagesManagementService = new UserMessagesManagementService(statistics, messenger, settingsManager);

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

        var services = new BotServices(settings,
            statistics,
            twitchApi,
            chatDecorationsProvider,
            audienceTracker,
            chatHistoryManager,
            broadcastScheduler,
            commandProcessor,
            streamStatusManager,
            userMessagesManagementService);

        return new(services, twitchClient, messenger);
    }
}
