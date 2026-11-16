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

    public YouTubeChatCursorToken? CursorToken { get; private set; }

    public YouTubeChatTransportKind ActiveTransport { get; private set; }

    public int SeenCount => _seenIds.Count;

    public CursorBatchResult ProcessBatch(YouTubeLiveChatBatch batch)
    {
        var result = FilterBatch(batch);

        foreach (var item in result.HistoricalItems)
        {
            if (item.Kind != YouTubeLiveChatItemKind.ChatEnded)
            {
                _seenIds.TryAdd(item.Id);
            }
        }

        foreach (var item in result.LiveItems)
        {
            if (item.Kind != YouTubeLiveChatItemKind.ChatEnded)
            {
                _seenIds.TryAdd(item.Id);
            }
        }

        CompleteBatch(batch, result.LiveItems.Any(static item => item.Kind != YouTubeLiveChatItemKind.ChatEnded));
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
            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                live.Add(item);
                continue;
            }

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

    public void CompleteBatch(YouTubeLiveChatBatch batch, bool sawLiveItems)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Transport != YouTubeChatTransportKind.None)
        {
            ActiveTransport = batch.Transport;
        }

        var isEnded = batch.OfflineAtUtc is not null
            || batch.Items.Any(static item => item.Kind == YouTubeLiveChatItemKind.ChatEnded);

        if (isEnded)
        {
            PageToken = null;
            CursorToken = null;
        }
        else if (batch.NextCursor is not null)
        {
            CursorToken = batch.NextCursor;
            PageToken = batch.NextCursor.Value;
        }
        else if (!string.IsNullOrEmpty(batch.NextPageToken))
        {
            PageToken = batch.NextPageToken;
            CursorToken = ActiveTransport != YouTubeChatTransportKind.None
                ? new(ActiveTransport, YouTubeChatMode.LiveChat, batch.NextPageToken)
                : null;
        }

        if (sawLiveItems)
        {
            HasTransitionedToLive = true;
        }
    }

    public YouTubeChatCursorToken? GetCursorTokenFor(YouTubeChatTransportKind nextTransport, YouTubeChatMode nextMode)
    {
        if (CursorToken is null
            || string.IsNullOrWhiteSpace(CursorToken.Value)
            || !IsCompatible(CursorToken.Transport, nextTransport))
        {
            return null;
        }

        if (IsOfficial(nextTransport) || CursorToken.Mode == nextMode)
        {
            return CursorToken;
        }

        return null;
    }

    public void CompleteBatch(string? nextPageToken, bool sawLiveItems)
    {
        if (!string.IsNullOrEmpty(nextPageToken))
        {
            PageToken = nextPageToken;
            if (ActiveTransport != YouTubeChatTransportKind.None)
            {
                CursorToken = new(ActiveTransport, YouTubeChatMode.LiveChat, nextPageToken);
            }
        }

        if (sawLiveItems)
        {
            HasTransitionedToLive = true;
        }
    }

    public void ResetAfterInvalidToken(DateTimeOffset newBoundaryUtc)
    {
        PageToken = null;
        CursorToken = null;
        HasTransitionedToLive = false;
        BoundaryUtc = newBoundaryUtc.ToUniversalTime();
    }

    public bool PrepareForTransport(
        YouTubeChatTransportKind nextTransport,
        YouTubeChatMode nextMode,
        DateTimeOffset nowUtc)
    {
        if (nextTransport == YouTubeChatTransportKind.None)
        {
            return false;
        }

        if (ActiveTransport == YouTubeChatTransportKind.None)
        {
            ActiveTransport = nextTransport;
            if (CursorToken is not null && !IsCompatible(CursorToken.Transport, nextTransport))
            {
                CursorToken = null;
                PageToken = null;
            }

            return false;
        }

        if (ActiveTransport == nextTransport)
        {
            if (CursorToken is not null && CursorToken.Mode != nextMode)
            {
                CursorToken = null;
                PageToken = null;
            }

            return false;
        }

        if (IsCompatible(ActiveTransport, nextTransport))
        {
            ActiveTransport = nextTransport;

            if (CursorToken is not null)
            {
                CursorToken = CursorToken with { Transport = nextTransport };
            }

            return false;
        }

        var gapDetected = SeenCount > 0 || HasTransitionedToLive;

        _seenIds.Clear();
        PageToken = null;
        CursorToken = null;
        HasTransitionedToLive = false;
        ActiveTransport = nextTransport;

        if (gapDetected)
        {
            BoundaryUtc = nowUtc.ToUniversalTime();
        }

        return gapDetected;
    }

    private static bool IsCompatible(YouTubeChatTransportKind left, YouTubeChatTransportKind right) =>
        left == right
        || (IsOfficial(left) && IsOfficial(right));

    private static bool IsOfficial(YouTubeChatTransportKind transport) =>
        transport is YouTubeChatTransportKind.OfficialGrpc or YouTubeChatTransportKind.OfficialRest;
}
