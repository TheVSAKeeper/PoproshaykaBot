using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatPipelineHandler :
    IEventHandler<RawChatMessageReceived>,
    IEventHandler<BotJoinedChannel>,
    IEventSubscriber,
    IChannelProvider,
    IDisposable
{
    private const string BotFallbackDisplayName = "PoproshaykaBot";
    private const string LocalMessageIdPrefix = "local-";

    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly PlatformChannelRegistry _channels;
    private readonly SettingsManager _settingsManager;
    private readonly ObsChatStore _obsChatStore;
    private readonly CommandSettingsStore _commandSettingsStore;
    private readonly AudienceTracker _audienceTracker;
    private readonly ChatCommandProcessor _commandProcessor;
    private readonly CommandResponseTracker _commandResponseTracker;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ChatPipelineHandler> _logger;
    private readonly List<IDisposable> _subscriptions;

    public ChatPipelineHandler(
        IChatPlatformRegistry platformRegistry,
        PlatformChannelRegistry channels,
        SettingsManager settingsManager,
        ObsChatStore obsChatStore,
        CommandSettingsStore commandSettingsStore,
        AudienceTracker audienceTracker,
        ChatCommandProcessor commandProcessor,
        CommandResponseTracker commandResponseTracker,
        IEventBus eventBus,
        ILogger<ChatPipelineHandler> logger)
    {
        _platformRegistry = platformRegistry;
        _channels = channels;
        _settingsManager = settingsManager;
        _obsChatStore = obsChatStore;
        _commandSettingsStore = commandSettingsStore;
        _audienceTracker = audienceTracker;
        _commandProcessor = commandProcessor;
        _commandResponseTracker = commandResponseTracker;
        _eventBus = eventBus;
        _logger = logger;

        _subscriptions =
        [
            eventBus.Subscribe<RawChatMessageReceived>(this),
            eventBus.Subscribe<BotJoinedChannel>(this),
        ];
    }

    public string? GetChannel(StreamingPlatform platform) => _channels.GetChannel(platform);

    string? IChannelProvider.Channel => GetChannel(StreamingPlatform.Twitch);

    public void Reset()
    {
        _channels.ClearAll();
        _audienceTracker.ClearAll();
        _commandResponseTracker.Clear();
    }

    public void Reset(StreamingPlatform platform)
    {
        _channels.Clear(platform);
        _audienceTracker.Clear(platform);
        _commandResponseTracker.Clear(platform);
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
        _channels.SetChannel(@event.Platform, @event.Channel);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(RawChatMessageReceived @event, CancellationToken cancellationToken)
    {
        var chatMessage = @event.Message;

        _channels.SetChannelIfUnset(chatMessage.Platform, chatMessage.Channel);

        if (!_platformRegistry.TryGet(chatMessage.Platform, out var platform))
        {
            _logger.LogWarning("Сообщение чата для незарегистрированной площадки {Platform} проигнорировано", chatMessage.Platform);
            return;
        }

        var messages = _settingsManager.Current.ChatMessages;
        var obsChat = _obsChatStore.Load();
        var audience = _audienceTracker.ForPlatform(chatMessage.Platform);

        var isFirstSeen = !chatMessage.IsBot
                          && audience.OnUserMessage(chatMessage.UserId, chatMessage.DisplayName);

        var status = GetUserStatusFlags(chatMessage);

        var badgesKvp = chatMessage.Badges
            .Select(b => new KeyValuePair<string, string>(b.SetId, b.BadgeId))
            .ToList();

        var historyEntry = new ChatMessageData
        {
            Platform = chatMessage.Platform,
            MessageId = chatMessage.Id,
            Timestamp = @event.Timestamp.UtcDateTime,
            UserId = chatMessage.UserId,
            DisplayName = chatMessage.DisplayName,
            Message = chatMessage.Message,
            MessageType = chatMessage.IsBot ? ChatMessageType.BotResponse : ChatMessageType.UserMessage,
            Status = status,
            IsFirstTime = isFirstSeen,
            Color = chatMessage.Color,

            Emotes = platform.Decorations.ExtractEmotes(chatMessage, obsChat.EmoteSizePixels),
            Badges = badgesKvp,
            BadgeUrls = platform.Decorations.ExtractBadgeUrls(chatMessage.Badges, obsChat.BadgeSizePixels),
        };

        var commandResponse = chatMessage.IsBot
            ? _commandResponseTracker.TryConsume(chatMessage.Platform, chatMessage.Id)
            : null;

        _ = _eventBus.PublishAsync(new ChatMessageReceived(chatMessage.Platform,
            chatMessage.Channel,
            chatMessage.Id,
            chatMessage.UserId,
            chatMessage.Username,
            chatMessage.DisplayName,
            chatMessage.Message,
            status,
            isFirstSeen,
            historyEntry,
            chatMessage.IsBot,
            commandResponse), cancellationToken);

        if (chatMessage.IsBot)
        {
            _logger.LogDebug("[Бот, эхо] {DisplayName}: {Message}", chatMessage.DisplayName, chatMessage.Message);
            return;
        }

        var context = new CommandContext
        {
            Platform = chatMessage.Platform,
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
        var canonical = commandResult.Canonical ?? string.Empty;

        var target = response != null
            ? _commandSettingsStore.Load().ResolveResponseTarget(canonical)
            : CommandResponseTarget.None;

        if (messages.WelcomeEnabled && isFirstSeen)
        {
            var welcomeMessage = audience.CreateWelcome(chatMessage.DisplayName);

            if (!string.IsNullOrWhiteSpace(welcomeMessage))
            {
                if (response != null && target.GoesToChat())
                {
                    response = response with { Text = $"{welcomeMessage} {response.Text}" };
                }
                else
                {
                    platform.Messenger.Reply(chatMessage.Id, chatMessage.DisplayName, welcomeMessage);
                }
            }
        }

        if (response != null)
        {
            await DeliverCommandResponseAsync(platform,
                response,
                canonical,
                target,
                chatMessage,
                context,
                cancellationToken);
        }

        _logger.LogDebug("[Бот] сообщение от {DisplayName} обработано ({Length} симв.)", chatMessage.DisplayName, chatMessage.Message.Length);
    }

    private async Task DeliverCommandResponseAsync(
        IChatPlatform platform,
        OutgoingMessage response,
        string canonical,
        CommandResponseTarget target,
        ChatMessage source,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var decision = CommandResponseRouting.Resolve(target, response, context.MessageId, platform.Capabilities);

        if (decision.Action == CommandDeliveryAction.None)
        {
            _logger.LogDebug("Ответ команды {Canonical} не доставлен: цель ответа – молча", canonical);
            return;
        }

        if (target.WhispersToCaller() && (platform.Capabilities & PlatformCapabilities.Whisper) == PlatformCapabilities.None)
        {
            _logger.LogDebug("Ответ команды {Canonical} перенаправлен на публичный отклик: площадка {Platform} не поддерживает личные сообщения",
                canonical,
                platform.Id);
        }

        var mark = new CommandResponseMark(canonical, target);

        switch (decision.Action)
        {
            case CommandDeliveryAction.Whisper:
                platform.Messenger.Whisper(context.UserId, context.MessageId, context.DisplayName, response.Text, mark);
                break;

            case CommandDeliveryAction.ReplyThread:
                platform.Messenger.Reply(decision.ReplyToMessageId ?? context.MessageId, context.DisplayName, response.Text, mark);
                break;

            case CommandDeliveryAction.MentionSend:
                platform.Messenger.Send(CommandResponseRouting.PrependCallerMention(context.DisplayName, response.Text), mark);
                break;

            case CommandDeliveryAction.Send:
                platform.Messenger.Send(response.Text, mark);
                break;

            case CommandDeliveryAction.OverlayOnly:
                await PublishOverlayOnlyResponseAsync(platform, response, source, mark, cancellationToken);
                break;
        }
    }

    private Task PublishOverlayOnlyResponseAsync(
        IChatPlatform platform,
        OutgoingMessage response,
        ChatMessage source,
        CommandResponseMark mark,
        CancellationToken cancellationToken)
    {
        var botIdentity = platform.BotIdentity;
        var userId = botIdentity?.UserId ?? string.Empty;
        var displayName = string.IsNullOrWhiteSpace(botIdentity?.DisplayName)
            ? BotFallbackDisplayName
            : botIdentity.DisplayName;
        var messageId = $"{LocalMessageIdPrefix}{Guid.NewGuid():N}";

        var historyEntry = new ChatMessageData
        {
            Platform = source.Platform,
            MessageId = messageId,
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            DisplayName = displayName,
            Message = response.Text,
            MessageType = ChatMessageType.BotResponse,
            Status = UserStatus.None,
        };

        return _eventBus.PublishAsync(new ChatMessageReceived(source.Platform,
            source.Channel,
            messageId,
            userId,
            displayName,
            displayName,
            response.Text,
            UserStatus.None,
            false,
            historyEntry,
            true,
            mark), cancellationToken);
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
