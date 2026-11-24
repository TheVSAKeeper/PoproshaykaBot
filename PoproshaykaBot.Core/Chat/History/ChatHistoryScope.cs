using PoproshaykaBot.Core.Platforms;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.Chat.History;

public sealed record ChatHistoryScope
{
    private readonly string _cacheFileName;

    public ChatHistoryScope(
        StreamingPlatform platform,
        string channelId,
        string? sessionScopeId = null,
        string? channelName = null)
    {
        Platform = platform;
        ChannelName = (channelName ?? string.Empty).Trim();
        ChannelId = NormalizeChannelId(platform, channelId, ChannelName);
        SessionScopeId = string.IsNullOrWhiteSpace(sessionScopeId) ? null : sessionScopeId.Trim();

        var key = SessionScopeId is null
            ? $"{Platform}:{ChannelId}"
            : $"{Platform}:{ChannelId}:{SessionScopeId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        _cacheFileName = Convert.ToHexStringLower(hash) + ".json";
    }

    public StreamingPlatform Platform { get; }

    public string ChannelId { get; }

    public string? SessionScopeId { get; }

    public string ChannelName { get; }

    public string CanonicalKey => SessionScopeId is null
        ? $"{Platform}:{ChannelId}"
        : $"{Platform}:{ChannelId}:{SessionScopeId}";

    public string ComputeCacheFileName() => _cacheFileName;

    public bool Equals(ChatHistoryScope? other)
    {
        if (other is null)
        {
            return false;
        }

        return Platform == other.Platform
            && string.Equals(ChannelId, other.ChannelId, StringComparison.Ordinal)
            && string.Equals(SessionScopeId, other.SessionScopeId, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Platform,
            StringComparer.Ordinal.GetHashCode(ChannelId),
            SessionScopeId is null ? 0 : StringComparer.Ordinal.GetHashCode(SessionScopeId));
    }

    public static ChatHistoryScope ForTwitch(string channelId, string channelName)
    {
        return new(StreamingPlatform.Twitch, channelId, null, channelName);
    }

    public static ChatHistoryScope ForYouTube(string channelId, string? liveChatId, string channelName)
    {
        return new(StreamingPlatform.YouTube, channelId, liveChatId, channelName);
    }

    public static ChatHistoryScope FromMessage(ChatMessageData message, string fallbackChannel = "")
    {
        ArgumentNullException.ThrowIfNull(message);

        var channelName = !string.IsNullOrWhiteSpace(message.Channel) ? message.Channel : fallbackChannel;
        var channelId = !string.IsNullOrWhiteSpace(message.ChannelId) ? message.ChannelId : channelName;
        var sessionScopeId = message.ChatSessionId;

        return new(message.Platform, channelId, sessionScopeId, channelName);
    }

    private static string NormalizeChannelId(StreamingPlatform platform, string? channelId, string channelName)
    {
        var raw = string.IsNullOrWhiteSpace(channelId) ? channelName : channelId;
        var trimmed = raw.Trim();

        if (platform == StreamingPlatform.Twitch)
        {
            return trimmed.ToLowerInvariant();
        }

        return trimmed;
    }
}
