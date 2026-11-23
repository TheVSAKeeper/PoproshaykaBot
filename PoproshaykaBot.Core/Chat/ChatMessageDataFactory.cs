using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat;

internal static class ChatMessageDataFactory
{
    public static ChatMessageData Create(
        ChatMessage chatMessage,
        IChatPlatform platform,
        ObsChatSettings obsChat,
        DateTime timestampUtc,
        bool isFirstTime,
        bool isHistorical,
        CommandResponseMark? commandResponse = null,
        Guid? broadcastId = null,
        string? streamId = null,
        DateTime? streamStartedAtUtc = null,
        string? streamTitle = null)
    {
        ArgumentNullException.ThrowIfNull(chatMessage);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(obsChat);

        var badges = chatMessage.Badges
            .Select(static b => new KeyValuePair<string, string>(b.SetId, b.BadgeId))
            .ToList();

        return new()
        {
            Platform = chatMessage.Platform,
            Channel = chatMessage.Channel,
            ChannelId = chatMessage.ChannelId,
            ChatSessionId = chatMessage.ChatSessionId,
            BroadcastId = broadcastId,
            StreamId = string.IsNullOrWhiteSpace(streamId) ? null : streamId.Trim(),
            StreamStartedAtUtc = streamStartedAtUtc is { } started && started != default ? EnsureUtc(started) : null,
            StreamTitle = string.IsNullOrWhiteSpace(streamTitle) ? null : streamTitle.Trim(),
            MessageId = chatMessage.Id,
            Timestamp = EnsureUtc(timestampUtc),
            UserId = chatMessage.UserId,
            DisplayName = chatMessage.DisplayName,
            Message = chatMessage.Message,
            MessageType = chatMessage.IsBot ? ChatMessageType.BotResponse : ChatMessageType.UserMessage,
            Status = GetUserStatusFlags(chatMessage),
            IsFirstTime = isFirstTime,
            IsHistorical = isHistorical,
            IsBot = chatMessage.IsBot,
            CommandResponse = commandResponse,
            Color = chatMessage.Color,
            Emotes = platform.Decorations.ExtractEmotes(chatMessage, obsChat.EmoteSizePixels),
            Badges = badges,
            BadgeUrls = platform.Decorations.ExtractBadgeUrls(chatMessage.Badges, obsChat.BadgeSizePixels),
        };
    }

    public static UserStatus GetUserStatusFlags(ChatMessage chatMessage)
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

    internal static DateTime EnsureUtc(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return default;
        }

        return timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        };
    }

    internal static DateTimeOffset ToUtcOffset(DateTime timestamp)
    {
        return new(EnsureUtc(timestamp), TimeSpan.Zero);
    }

    internal static long ToUtcTicks(DateTime timestamp)
    {
        return EnsureUtc(timestamp).Ticks;
    }
}
