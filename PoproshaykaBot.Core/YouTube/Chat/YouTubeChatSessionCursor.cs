using PoproshaykaBot.Core.YouTube.Chat.Transport;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed record CursorBatchResult(
    IReadOnlyList<YouTubeLiveChatItem> LiveItems,
    IReadOnlyList<YouTubeLiveChatItem> HistoricalItems);

internal sealed class YouTubeChatSessionCursor(
    string liveChatId,
    DateTimeOffset boundaryUtc,
    int seenCapacity = BoundedSeenMessageIdSet.DefaultCapacity)
{
    private readonly BoundedSeenMessageIdSet _seenIds = new(seenCapacity);

    public string LiveChatId { get; } = liveChatId;

    public DateTimeOffset BoundaryUtc { get; private set; } = boundaryUtc.ToUniversalTime();

    public bool HasTransitionedToLive { get; private set; }

    public string? PageToken { get; private set; }

    public int SeenCount => _seenIds.Count;

    public CursorBatchResult ProcessBatch(YouTubeLiveChatBatch batch)
    {
        var result = FilterBatch(batch);

        foreach (var item in result.HistoricalItems)
        {
            _seenIds.TryAdd(item.Id);
        }

        foreach (var item in result.LiveItems)
        {
            _seenIds.TryAdd(item.Id);
        }

        CompleteBatch(batch.NextPageToken, result.LiveItems.Count > 0);
        return result;
    }

    public CursorBatchResult FilterBatch(YouTubeLiveChatBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var live = new List<YouTubeLiveChatItem>(batch.Items.Count);
        var historical = HasTransitionedToLive ? null : new List<YouTubeLiveChatItem>();
        var batchSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in batch.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id)
                || _seenIds.Contains(item.Id)
                || !batchSeen.Add(item.Id))
            {
                continue;
            }

            if (!HasTransitionedToLive)
            {
                if (item.PublishedAtUtc.HasValue && item.PublishedAtUtc.Value.ToUniversalTime() >= BoundaryUtc)
                {
                    live.Add(item);
                }
                else
                {
                    historical!.Add(item);
                }
            }
            else
            {
                live.Add(item);
            }
        }

        return new(live, (IReadOnlyList<YouTubeLiveChatItem>?)historical ?? []);
    }

    public void MarkSeen(string? messageId)
    {
        _seenIds.TryAdd(messageId);
    }

    public void CompleteBatch(string? nextPageToken, bool sawLiveItems)
    {
        if (!string.IsNullOrEmpty(nextPageToken))
        {
            PageToken = nextPageToken;
        }

        if (sawLiveItems)
        {
            HasTransitionedToLive = true;
        }
    }

    public void ResetAfterInvalidToken(DateTimeOffset newBoundaryUtc)
    {
        PageToken = null;
        HasTransitionedToLive = false;
        BoundaryUtc = newBoundaryUtc.ToUniversalTime();
    }
}
