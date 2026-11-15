namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class BoundedSeenMessageIdSet
{
    public const int DefaultCapacity = 4_096;

    private readonly int _capacity;
    private readonly HashSet<string> _set;
    private readonly Queue<string> _order;

    public BoundedSeenMessageIdSet(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _set = new(capacity, StringComparer.Ordinal);
        _order = new(capacity);
    }

    public int Count => _set.Count;

    public bool TryAdd(string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        if (_set.Contains(messageId))
        {
            return false;
        }

        if (_order.Count >= _capacity)
        {
            var oldest = _order.Dequeue();
            _set.Remove(oldest);
        }

        _set.Add(messageId);
        _order.Enqueue(messageId);
        return true;
    }

    public bool Contains(string? messageId)
    {
        return !string.IsNullOrWhiteSpace(messageId) && _set.Contains(messageId);
    }
}
