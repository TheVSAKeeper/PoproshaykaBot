using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public sealed class TwitchChatHandler :
    IChannelProvider,
    IEventHandler<RawChatMessageReceived>,
    IEventHandler<BotJoinedChannel>,
    IEventSubscriber,
    IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly ObsChatStore _obsChatStore;
    private readonly AudienceTracker _audienceTracker;
    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly ChatCommandProcessor _commandProcessor;
    private readonly TwitchChatMessenger _messenger;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TwitchChatHandler> _logger;
    private readonly List<IDisposable> _subscriptions;

    public TwitchChatHandler(
        SettingsManager settingsManager,
        ObsChatStore obsChatStore,
        AudienceTracker audienceTracker,
        ChatDecorationsProvider chatDecorationsProvider,
        ChatCommandProcessor commandProcessor,
        TwitchChatMessenger messenger,
        IEventBus eventBus,
        ILogger<TwitchChatHandler> logger)
    {
        _settingsManager = settingsManager;
        _obsChatStore = obsChatStore;
        _audienceTracker = audienceTracker;
        _chatDecorations = chatDecorationsProvider;
        _commandProcessor = commandProcessor;
        _messenger = messenger;
        _eventBus = eventBus;
        _logger = logger;

        _subscriptions =
        [
            eventBus.Subscribe<RawChatMessageReceived>(this),
            eventBus.Subscribe<BotJoinedChannel>(this),
        ];
    }

    public string? Channel { get; private set; }

    public void Reset()
    {
        Channel = null;
        _audienceTracker.ClearAll();
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    public Task HandleAsync(BotJoinedChannel @event, CancellationToken cancellationToken)
    {
        Channel = @event.Channel;
        return Task.CompletedTask;
    }

    public async Task HandleAsync(RawChatMessageReceived @event, CancellationToken cancellationToken)
    {
        var chatMessage = @event.Message;

        Channel ??= chatMessage.Channel;

        var settings = _settingsManager.Current.Twitch;

        var isFirstSeen = !chatMessage.IsBot
                          && _audienceTracker.OnUserMessage(chatMessage.UserId, chatMessage.DisplayName);

        var status = GetUserStatusFlags(chatMessage);

        var badgesKvp = chatMessage.Badges
            .Select(b => new KeyValuePair<string, string>(b.SetId, b.BadgeId))
            .ToList();

        var historyEntry = new ChatMessageData
        {
            MessageId = chatMessage.Id,
            Timestamp = @event.Timestamp.UtcDateTime,
            UserId = chatMessage.UserId,
            DisplayName = chatMessage.DisplayName,
            Message = chatMessage.Message,
            MessageType = chatMessage.IsBot ? ChatMessageType.BotResponse : ChatMessageType.UserMessage,
            Status = status,
            IsFirstTime = isFirstSeen,

            Emotes = _chatDecorations.ExtractEmotes(chatMessage, _obsChatStore.Load().EmoteSizePixels),
            Badges = badgesKvp,
            BadgeUrls = _chatDecorations.ExtractBadgeUrls(chatMessage.Badges, _obsChatStore.Load().BadgeSizePixels),
        };

        _ = _eventBus.PublishAsync(new ChatMessageReceived(chatMessage.Channel,
            chatMessage.Id,
            chatMessage.UserId,
            chatMessage.Username,
            chatMessage.DisplayName,
            chatMessage.Message,
            status,
            isFirstSeen,
            historyEntry,
            chatMessage.IsBot), cancellationToken);

        if (chatMessage.IsBot)
        {
            _logger.LogDebug("[Бот, эхо] {DisplayName}: {Message}", chatMessage.DisplayName, chatMessage.Message);
            return;
        }

        var context = new CommandContext
        {
            Channel = chatMessage.Channel,
            MessageId = chatMessage.Id,
            UserId = chatMessage.UserId,
            Username = chatMessage.Username,
            DisplayName = chatMessage.DisplayName,
            IsBroadcaster = chatMessage.IsBroadcaster,
            IsModerator = chatMessage.IsModerator,
        };

        var commandResult = await _commandProcessor.TryProcessAsync(chatMessage.Message, context, cancellationToken);
        var response = commandResult.Response;

        if (settings.Messages.WelcomeEnabled && isFirstSeen)
        {
            var welcomeMessage = _audienceTracker.CreateWelcome(chatMessage.DisplayName);

            if (!string.IsNullOrWhiteSpace(welcomeMessage))
            {
                if (commandResult.IsCommand && response != null)
                {
                    response = response with { Text = $"{welcomeMessage} {response.Text}" };
                }
                else if (!commandResult.IsCommand)
                {
                    _messenger.Reply(chatMessage.Id, welcomeMessage);
                }
            }
        }

        if (response != null)
        {
            switch (response.Delivery)
            {
                case DeliveryType.Reply:
                    _messenger.Reply(response.ReplyToMessageId ?? context.MessageId, response.Text);
                    break;

                default:
                    _messenger.Send(response.Text);
                    break;
            }
        }

        _logger.LogDebug("[Бот] {DisplayName}: {Message}", chatMessage.DisplayName, chatMessage.Message);
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
}
