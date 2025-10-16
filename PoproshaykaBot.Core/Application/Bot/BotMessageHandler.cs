using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Application.Statistics;
using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Domain.Models.Settings;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace PoproshaykaBot.Core.Application.Bot;

/// <summary>
/// Обработчик сообщений чата.
/// Отвечает за обработку входящих сообщений, команд, приветствий и статистики.
/// </summary>
public class BotMessageHandler
{
    private readonly StatisticsCollector _statisticsCollector;
    private readonly AudienceTracker _audienceTracker;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly ChatCommandProcessor _commandProcessor;
    private readonly TwitchChatMessenger _messenger;
    private readonly TwitchSettings _settings;

    public BotMessageHandler(
        StatisticsCollector statisticsCollector,
        AudienceTracker audienceTracker,
        ChatHistoryManager chatHistoryManager,
        ChatDecorationsProvider chatDecorations,
        ChatCommandProcessor commandProcessor,
        TwitchChatMessenger messenger,
        TwitchSettings settings)
    {
        _statisticsCollector = statisticsCollector ?? throw new ArgumentNullException(nameof(statisticsCollector));
        _audienceTracker = audienceTracker ?? throw new ArgumentNullException(nameof(audienceTracker));
        _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
        _chatDecorations = chatDecorations ?? throw new ArgumentNullException(nameof(chatDecorations));
        _commandProcessor = commandProcessor ?? throw new ArgumentNullException(nameof(commandProcessor));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Обрабатывает входящее сообщение из чата.
    /// </summary>
    public void HandleMessage(OnMessageReceivedArgs e)
    {
        if (e?.ChatMessage == null)
        {
            return;
        }

        _statisticsCollector.TrackMessage(e.ChatMessage.UserId, e.ChatMessage.Username);

        var isFirstSeen = _audienceTracker.OnUserMessage(e.ChatMessage.UserId, e.ChatMessage.DisplayName);

        var userMessage = CreateChatMessageData(e, isFirstSeen);
        _chatHistoryManager.AddMessage(userMessage);

        ProcessWelcomeIfNeeded(e, isFirstSeen);

        ProcessCommand(e);
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

    private ChatMessageData CreateChatMessageData(OnMessageReceivedArgs e, bool isFirstSeen)
    {
        return new()
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = e.ChatMessage.DisplayName,
            Message = e.ChatMessage.Message,
            MessageType = ChatMessageType.UserMessage,
            Status = GetUserStatusFlags(e.ChatMessage),
            IsFirstTime = isFirstSeen,
            Emotes = _chatDecorations.ExtractEmotes(e.ChatMessage, _settings.ObsChat.EmoteSizePixels),
            Badges = e.ChatMessage.Badges,
            BadgeUrls = _chatDecorations.ExtractBadgeUrls(e.ChatMessage.Badges, _settings.ObsChat.BadgeSizePixels),
        };
    }

    private void ProcessWelcomeIfNeeded(OnMessageReceivedArgs e, bool isFirstSeen)
    {
        if (!_settings.Messages.WelcomeEnabled || !isFirstSeen)
        {
            return;
        }

        var welcomeMessage = _audienceTracker.CreateWelcome(e.ChatMessage.DisplayName);

        if (!string.IsNullOrWhiteSpace(welcomeMessage))
        {
            _messenger.Reply(e.ChatMessage.Channel, e.ChatMessage.Id, welcomeMessage);
        }
    }

    private void ProcessCommand(OnMessageReceivedArgs e)
    {
        var context = new CommandContext
        {
            Channel = e.ChatMessage.Channel,
            MessageId = e.ChatMessage.Id,
            UserId = e.ChatMessage.UserId,
            Username = e.ChatMessage.Username,
            DisplayName = e.ChatMessage.DisplayName,
        };

        if (!_commandProcessor.TryProcess(e.ChatMessage.Message, context, out var response))
        {
            return;
        }

        if (response == null)
        {
            return;
        }

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
}
