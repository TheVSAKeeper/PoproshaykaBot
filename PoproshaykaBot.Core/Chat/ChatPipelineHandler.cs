using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
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
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly IBroadcastState? _broadcastState;
    private readonly ChatHistoryManager? _chatHistoryManager;
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
        ILogger<ChatPipelineHandler> logger,
        ITargetChannelProvider? targetChannelProvider = null,
        IBroadcastState? broadcastState = null,
        ChatHistoryManager? chatHistoryManager = null)
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
        _targetChannelProvider = targetChannelProvider;
        _broadcastState = broadcastState;
        _chatHistoryManager = chatHistoryManager;

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

    public void ResetChannel(StreamingPlatform platform, ChannelKey? previousChannel)
    {
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
        var origin = chatMessage.Origin;
        var recordsUserData = _targetChannelProvider?.Current.RecordsUserData;
        if (origin is { IsCurrent: false })
        {
            return;
        }

        _channels.SetChannelIfUnset(chatMessage.Platform, chatMessage.Channel);

        if (!_platformRegistry.TryGet(chatMessage.Platform, out var platform))
        {
            _logger.LogWarning("Сообщение чата для незарегистрированной площадки {Platform} проигнорировано", chatMessage.Platform);
            return;
        }

        var messages = _settingsManager.Current.ChatMessages;
        var obsChat = _obsChatStore.Load();
        var audience = _audienceTracker.ForIncomingMessage(chatMessage.Platform, @event.ChannelKey, origin);
        if (audience is null)
        {
            return;
        }

        var liveStream = platform.StreamStatus is { CurrentStatus: StreamStatus.Online } streamStatus
            ? streamStatus.CurrentStream
            : null;
        var broadcastId = liveStream is not null ? _broadcastState?.CurrentBroadcastId : null;

        var seenBeforeInStream = !chatMessage.IsBot
                                 && !@event.IsReplacement
                                 && (liveStream is not null || !string.IsNullOrWhiteSpace(chatMessage.ChatSessionId))
                                 && _chatHistoryManager?.HasUserMessageInCurrentStream(
                                     chatMessage.Platform,
                                     @event.ChannelKey?.ChannelId ?? chatMessage.ChannelId,
                                     chatMessage.UserId,
                                     liveStream?.Id,
                                     broadcastId,
                                     liveStream?.StartedAt ?? default,
                                     chatMessage.ChatSessionId) == true;

        var isFirstSeen = !chatMessage.IsBot
                          && !@event.IsReplacement
                          && audience.OnUserMessage(chatMessage.UserId, chatMessage.DisplayName)
                          && !seenBeforeInStream;

        var commandResponse = chatMessage.IsBot
            ? _commandResponseTracker.TryConsume(chatMessage.Platform, chatMessage.Id, @event.ChannelKey, chatMessage.ChatSessionId)
            : null;

        var historyEntry = ChatMessageDataFactory.Create(
            chatMessage,
            platform,
            obsChat,
            @event.Timestamp.UtcDateTime,
            isFirstSeen,
            isHistorical: false,
            commandResponse,
            broadcastId,
            liveStream?.Id,
            liveStream?.StartedAt,
            liveStream?.Title);

        var status = historyEntry.Status;

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
            commandResponse,
            @event.IsReplacement,
            origin,
            recordsUserData), cancellationToken);

        if (chatMessage.IsBot || @event.IsReplacement)
        {
            if (chatMessage.IsBot)
            {
                _logger.LogDebug("[Бот, эхо] {DisplayName}: {Message}", chatMessage.DisplayName, chatMessage.Message);
            }

            return;
        }

        var context = new CommandContext
        {
            Platform = chatMessage.Platform,
            Channel = chatMessage.Channel,
            ChannelKey = @event.ChannelKey,
            Origin = origin,
            MessageId = chatMessage.Id,
            UserId = chatMessage.UserId,
            Username = chatMessage.Username,
            DisplayName = chatMessage.DisplayName,
            IsBroadcaster = chatMessage.IsBroadcaster,
            IsModerator = chatMessage.IsModerator,
            RecordsUserData = recordsUserData ?? true,
        };

        var commandResult = await _commandProcessor.TryProcessAsync(chatMessage.Message, context, cancellationToken);
        if (origin is { IsCurrent: false })
        {
            return;
        }

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
                    platform.Messenger.Reply(chatMessage.Id, chatMessage.DisplayName, welcomeMessage, category: ChatSendCategory.Greeting, origin: origin);
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
                platform.Messenger.Whisper(context.UserId, context.MessageId, context.DisplayName, response.Text, mark, ChatSendCategory.CommandReply, context.Origin);
                break;

            case CommandDeliveryAction.ReplyThread:
                platform.Messenger.Reply(decision.ReplyToMessageId ?? context.MessageId, context.DisplayName, response.Text, mark, ChatSendCategory.CommandReply, context.Origin);
                break;

            case CommandDeliveryAction.MentionSend:
                platform.Messenger.Send(CommandResponseRouting.PrependCallerMention(context.DisplayName, response.Text), mark, ChatSendCategory.CommandReply, context.Origin);
                break;

            case CommandDeliveryAction.Send:
                platform.Messenger.Send(response.Text, mark, ChatSendCategory.CommandReply, context.Origin);
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

        var liveStream = platform.StreamStatus is { CurrentStatus: StreamStatus.Online } streamStatus
            ? streamStatus.CurrentStream
            : null;
        var broadcastId = liveStream is not null ? _broadcastState?.CurrentBroadcastId : null;

        var historyEntry = new ChatMessageData
        {
            Platform = source.Platform,
            Channel = source.Channel,
            ChannelId = source.ChannelId,
            ChatSessionId = source.ChatSessionId,
            BroadcastId = broadcastId,
            StreamId = string.IsNullOrWhiteSpace(liveStream?.Id) ? null : liveStream.Id.Trim(),
            StreamStartedAtUtc = liveStream is { StartedAt: var startedAt } && startedAt != default
                ? ChatMessageDataFactory.EnsureUtc(startedAt)
                : null,
            StreamTitle = string.IsNullOrWhiteSpace(liveStream?.Title) ? null : liveStream.Title.Trim(),
            MessageId = messageId,
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            DisplayName = displayName,
            Message = response.Text,
            MessageType = ChatMessageType.BotResponse,
            Status = UserStatus.None,
            IsHistorical = false,
            IsBot = true,
            CommandResponse = mark,
        };

        if (source.Origin is { IsCurrent: false })
        {
            return Task.CompletedTask;
        }

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
            mark,
            Origin: source.Origin), cancellationToken);
    }
}
