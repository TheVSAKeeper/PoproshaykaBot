using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Users;
using System.Globalization;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;

public readonly record struct ChatFeedMessageKey(
    StreamingPlatform Platform,
    string ChannelId,
    string MessageId);

public abstract class ChatFeedItemViewModel : ObservableObject
{
    public virtual StreamingPlatform? PlatformFilterKey => null;
}

public sealed partial class ChatFeedMessageItemViewModel : ChatFeedItemViewModel
{
    private readonly Action<PlatformUserKey>? _openUser;

    [ObservableProperty]
    private string _messageText;

    public ChatFeedMessageItemViewModel(
        ChatFeedMessageKey key,
        string channel,
        DateTime timestampUtc,
        string displayName,
        string messageText,
        string userId,
        UserStatus status,
        bool isHistorical,
        bool isBot,
        bool isFirstTime,
        string authorColorHex,
        Action<PlatformUserKey>? openUser,
        string platformDisplayName,
        string? chatSessionId = null,
        Guid? broadcastId = null,
        string? streamId = null,
        DateTime? streamStartedAtUtc = null,
        string? streamTitle = null)
    {
        Key = key;
        Channel = channel;
        TimestampUtc = timestampUtc;
        TimestampText = timestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Пользователь" : displayName;
        _messageText = messageText;
        UserId = userId;
        UserKey = new PlatformUserKey(key.Platform, userId);
        PlatformDisplayName = platformDisplayName;
        ChatSessionId = chatSessionId;
        BroadcastId = broadcastId;
        StreamId = streamId;
        StreamStartedAtUtc = streamStartedAtUtc;
        StreamTitle = streamTitle;
        IsHistorical = isHistorical;
        IsBot = isBot;
        IsFirstTime = isFirstTime;
        RoleBadgeText = ResolveRoleBadge(status, isBot);
        AuthorBrush = TryCreateSafeBrush(authorColorHex);
        _openUser = openUser;
    }

    public ChatFeedMessageKey Key { get; }

    public string Channel { get; }

    public StreamingPlatform Platform => Key.Platform;

    public string PlatformDisplayName { get; }

    public string? ChatSessionId { get; }

    public Guid? BroadcastId { get; }

    public string? StreamId { get; }

    public DateTime? StreamStartedAtUtc { get; }

    public string? StreamTitle { get; }

    public override StreamingPlatform? PlatformFilterKey => Platform;

    public DateTime TimestampUtc { get; }

    public string TimestampText { get; }

    public string UserId { get; }

    public PlatformUserKey UserKey { get; }

    public string DisplayName { get; }

    public bool IsHistorical { get; }

    public bool IsBot { get; }

    public bool IsFirstTime { get; }

    public string? RoleBadgeText { get; }

    public bool HasRoleBadge => !string.IsNullOrEmpty(RoleBadgeText);

    public bool HasUserLink => !string.IsNullOrWhiteSpace(UserId);

    public Brush? AuthorBrush { get; }

    [RelayCommand]
    private void OpenUser()
    {
        if (HasUserLink)
        {
            _openUser?.Invoke(UserKey);
        }
    }

    public static ChatFeedMessageKey CreateKey(ChatMessageData entry)
    {
        var channelKey = !string.IsNullOrWhiteSpace(entry.ChannelId)
            ? entry.ChannelId
            : entry.Channel;

        return new ChatFeedMessageKey(entry.Platform, channelKey, entry.MessageId);
    }

    private static string? ResolveRoleBadge(UserStatus status, bool isBot)
    {
        if (isBot)
        {
            return "Бот";
        }

        if (status.HasFlag(UserStatus.Broadcaster))
        {
            return "Стример";
        }

        if (status.HasFlag(UserStatus.Moderator))
        {
            return "Мод";
        }

        if (status.HasFlag(UserStatus.Vip))
        {
            return "VIP";
        }

        return null;
    }

    private static Brush? TryCreateSafeBrush(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return null;
        }

        try
        {
            if (ColorConverter.ConvertFromString(colorHex) is not Color color)
            {
                return null;
            }

            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            if (luminance is < 0.25 or > 0.85)
            {
                return null;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public sealed class ChatFeedHistoryBoundaryItemViewModel : ChatFeedItemViewModel
{
    public string Label => "Загружена предыдущая история";
}

public sealed class ChatFeedStreamDividerItemViewModel(string key, string label, bool isStreamBoundary) : ChatFeedItemViewModel
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public bool IsStreamBoundary { get; } = isStreamBoundary;
}

public sealed class ChatFeedGapItemViewModel : ChatFeedItemViewModel
{
    public ChatFeedGapItemViewModel(
        StreamingPlatform platform,
        string channel,
        string liveChatId,
        DateTimeOffset detectedAtUtc,
        string platformDisplayName)
    {
        Platform = platform;
        PlatformDisplayName = platformDisplayName;
        Channel = channel;
        LiveChatId = liveChatId;
        DetectedAtUtc = detectedAtUtc;
        TimestampText = detectedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public StreamingPlatform Platform { get; }

    public string PlatformDisplayName { get; }

    public override StreamingPlatform? PlatformFilterKey => Platform;

    public string Channel { get; }

    public string LiveChatId { get; }

    public DateTimeOffset DetectedAtUtc { get; }

    public string TimestampText { get; }

    public string Text => "Пробел чтения чата – часть сообщений пропущена при переподключении";
}
