using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Chat;

public sealed class CommandResponseTracker(TimeProvider timeProvider)
{
    public const int MaxTrackedMessages = 256;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly Lock _lock = new();
    private readonly Dictionary<MessageKey, LinkedListNode<Entry>> _byMessageId = [];
    private readonly LinkedList<Entry> _order = new();

    public int PendingCount
    {
        get
        {
            var now = timeProvider.GetUtcNow();

            lock (_lock)
            {
                DropExpired(now);
                return _byMessageId.Count;
            }
        }
    }

    public void Expect(string messageId, CommandResponseMark mark)
    {
        Expect(StreamingPlatform.Twitch, messageId, mark);
    }

    public void Expect(StreamingPlatform platform, string messageId, CommandResponseMark mark, ChannelKey? channel = null, string? chatId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(mark);

        var key = new MessageKey(platform, channel, chatId, messageId);
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            DropExpired(now);

            if (_byMessageId.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _byMessageId.Remove(key);
            }

            while (_byMessageId.Count >= MaxTrackedMessages && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _byMessageId.Remove(oldest.Value.Key);
            }

            _byMessageId[key] = _order.AddLast(new Entry(key, mark, now + Lifetime));
        }
    }

    public CommandResponseMark? TryConsume(string messageId)
    {
        return TryConsume(StreamingPlatform.Twitch, messageId);
    }

    public CommandResponseMark? TryConsume(StreamingPlatform platform, string messageId, ChannelKey? channel = null, string? chatId = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var key = new MessageKey(platform, channel, chatId, messageId);
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            DropExpired(now);

            if (!_byMessageId.Remove(key, out var node))
            {
                return null;
            }

            _order.Remove(node);
            return node.Value.Mark;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _byMessageId.Clear();
            _order.Clear();
        }
    }

    public void Clear(StreamingPlatform platform)
    {
        lock (_lock)
        {
            var current = _order.First;

            while (current != null)
            {
                var next = current.Next;

                if (current.Value.Key.Platform == platform)
                {
                    _order.Remove(current);
                    _byMessageId.Remove(current.Value.Key);
                }

                current = next;
            }
        }
    }

    private void DropExpired(DateTimeOffset now)
    {
        while (_order.First is { } oldest && oldest.Value.ExpiresAt <= now)
        {
            _order.RemoveFirst();
            _byMessageId.Remove(oldest.Value.Key);
        }
    }

    private readonly record struct MessageKey(StreamingPlatform Platform, ChannelKey? Channel, string? ChatId, string MessageId);

    private sealed record Entry(MessageKey Key, CommandResponseMark Mark, DateTimeOffset ExpiresAt);
}
