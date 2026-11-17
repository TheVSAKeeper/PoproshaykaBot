using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatOrigin
{
    private int _closed;

    public ChatOrigin(ChannelKey channel, string chatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel.ChannelId, nameof(channel));
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);

        Channel = channel;
        ChatId = chatId;
    }

    public ChannelKey Channel { get; }
    public string ChatId { get; }
    public bool IsCurrent => Volatile.Read(ref _closed) == 0;

    internal void Close() => Interlocked.Exchange(ref _closed, 1);
}
