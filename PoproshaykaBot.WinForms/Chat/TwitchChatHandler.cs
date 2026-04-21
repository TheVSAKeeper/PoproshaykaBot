using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Users;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace PoproshaykaBot.WinForms.Chat;

public sealed class TwitchChatHandler : IChannelProvider
{
    private readonly TwitchClient _client;
    private readonly SettingsManager _settingsManager;
    private readonly AudienceTracker _audienceTracker;
    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly ChatCommandProcessor _commandProcessor;
    private readonly TwitchChatMessenger _messenger;
    private readonly IEventBus _eventBus;

    public TwitchChatHandler(
        TwitchClient twitchClient,
        SettingsManager settingsManager,
        AudienceTracker audienceTracker,
        ChatDecorationsProvider chatDecorationsProvider,
        ChatCommandProcessor commandProcessor,
        TwitchChatMessenger messenger,
        IEventBus eventBus)
    {
        _client = twitchClient;
        _settingsManager = settingsManager;
        _audienceTracker = audienceTracker;
        _chatDecorations = chatDecorationsProvider;
        _commandProcessor = commandProcessor;
        _messenger = messenger;
        _eventBus = eventBus;

        _client.OnLog += Client_OnLog;
        _client.OnConnected += Client_OnConnected;
        _client.OnJoinedChannel += Client_OnJoinedChannel;
        _client.OnMessageReceived += Client_OnMessageReceived;
    }

    public string? Channel { get; private set; }

    public void Reset()
    {
        Channel = null;
        _audienceTracker.ClearAll();
    }

    private void Client_OnLog(object? sender, OnLogArgs e)
    {
        var logMessage = $"{e.DateTime}: {e.BotUsername} - {e.Data}";
        Console.WriteLine(logMessage);
        PublishBotLog(logMessage);
    }

    private void Client_OnConnected(object? sender, OnConnectedArgs e)
    {
        var connectionMessage = "Подключен!";
        Console.WriteLine(connectionMessage);
        PublishBotLog(connectionMessage);
    }

    private void Client_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        Channel = e.Channel;

        var connectionMessage = $"Подключен к каналу {e.Channel}";
        Console.WriteLine(connectionMessage);
        PublishBotLog(connectionMessage);

        _ = _eventBus.PublishAsync(new BotJoinedChannel(e.Channel));
    }

    private void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        var settings = _settingsManager.Current.Twitch;

        var isFirstSeen = _audienceTracker.OnUserMessage(e.ChatMessage.UserId, e.ChatMessage.DisplayName);

        var status = GetUserStatusFlags(e.ChatMessage);

        var historyEntry = new ChatMessageData
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = e.ChatMessage.DisplayName,
            Message = e.ChatMessage.Message,
            MessageType = ChatMessageType.UserMessage,
            Status = status,
            IsFirstTime = isFirstSeen,

            Emotes = _chatDecorations.ExtractEmotes(e.ChatMessage, settings.ObsChat.EmoteSizePixels),
            Badges = e.ChatMessage.Badges,
            BadgeUrls = _chatDecorations.ExtractBadgeUrls(e.ChatMessage.Badges, settings.ObsChat.BadgeSizePixels),
        };

        _ = _eventBus.PublishAsync(new ChatMessageReceived(e.ChatMessage.Channel,
            e.ChatMessage.Id,
            e.ChatMessage.UserId,
            e.ChatMessage.Username,
            e.ChatMessage.DisplayName,
            e.ChatMessage.Message,
            status,
            isFirstSeen,
            historyEntry));

        var context = new CommandContext
        {
            Channel = e.ChatMessage.Channel,
            MessageId = e.ChatMessage.Id,
            UserId = e.ChatMessage.UserId,
            Username = e.ChatMessage.Username,
            DisplayName = e.ChatMessage.DisplayName,
            IsBroadcaster = e.ChatMessage.IsBroadcaster,
            IsModerator = e.ChatMessage.IsModerator,
        };

        var isCommand = _commandProcessor.TryProcess(e.ChatMessage.Message, context, out var response);

        if (settings.Messages.WelcomeEnabled && isFirstSeen)
        {
            var welcomeMessage = _audienceTracker.CreateWelcome(e.ChatMessage.DisplayName);

            if (!string.IsNullOrWhiteSpace(welcomeMessage))
            {
                if (isCommand && response != null)
                {
                    response = response with { Text = $"{welcomeMessage} {response.Text}" };
                }
                else if (!isCommand)
                {
                    _messenger.Reply(e.ChatMessage.Channel, e.ChatMessage.Id, welcomeMessage);
                }
            }
        }

        if (response != null)
        {
            switch (response.Delivery)
            {
                case DeliveryType.Reply:
                    _messenger.Reply(context.Channel, response.ReplyToMessageId ?? context.MessageId, response.Text);
                    break;

                case DeliveryType.Normal:
                default:
                    _messenger.Send(context.Channel, response.Text);
                    break;
            }
        }

        PublishBotLog(e.ChatMessage.DisplayName + ": " + e.ChatMessage.Message);
    }

    private static UserStatus GetUserStatusFlags(ChatMessage chatMessage)
    {
        var status = UserStatus.None;

        if (chatMessage.IsBroadcaster)
        {
            status |= UserStatus.Broadcaster;
        }

        if (chatMessage.IsModerator)
        {
            status |= UserStatus.Moderator;
        }

        if (chatMessage.IsVip)
        {
            status |= UserStatus.Vip;
        }

        if (chatMessage.IsSubscriber)
        {
            status |= UserStatus.Subscriber;
        }

        return status;
    }

    private void PublishBotLog(string message)
    {
        _ = _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Debug, "Bot", $"[Бот] {message}"));
    }
}
