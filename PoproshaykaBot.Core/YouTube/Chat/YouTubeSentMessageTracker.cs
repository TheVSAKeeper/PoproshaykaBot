namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class YouTubeSentMessageTracker : IYouTubeSentMessageTracker
{
    public const int DefaultCapacity = 1_024;

    private readonly Lock _sync = new();
    private readonly HashSet<(string? ChatId, string MessageId)> _sentIds = new(DefaultCapacity);
    private readonly Queue<(string? ChatId, string MessageId)> _sentOrder = new(DefaultCapacity);
    private readonly List<InFlightEntry> _inFlight = [];
    private readonly List<Waiter> _waiters = [];
    private long _nextSequence;

    public bool Contains(string messageId, string? liveChatId = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        lock (_sync)
        {
            return _sentIds.Contains((liveChatId, messageId));
        }
    }

    public void TrackSent(string messageId, string? liveChatId = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        lock (_sync)
        {
            TrackSentLocked(liveChatId, messageId);
            NotifyWaitersLocked(liveChatId, messageId);
        }
    }

    public YouTubeInFlightSendScope BeginInFlightSend(string liveChatId) => BeginInFlightSend(liveChatId, expectedNormalizedText: null);

    public YouTubeInFlightSendScope BeginInFlightSend(string liveChatId, string? expectedNormalizedText)
    {
        if (string.IsNullOrWhiteSpace(liveChatId))
        {
            return new();
        }

        var normalizedText = string.IsNullOrWhiteSpace(expectedNormalizedText)
            ? null
            : expectedNormalizedText.Trim();

        lock (_sync)
        {
            var seq = ++_nextSequence;
            _inFlight.Add(new(liveChatId, seq, normalizedText));
            return new(this, liveChatId, seq);
        }
    }

    public bool HasInFlightMatchingText(string liveChatId, long watermarkSeq, string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(liveChatId) || watermarkSeq <= 0 || string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        lock (_sync)
        {
            foreach (var entry in _inFlight)
            {
                if (string.Equals(entry.LiveChatId, liveChatId, StringComparison.Ordinal)
                    && entry.Sequence <= watermarkSeq
                    && string.Equals(entry.NormalizedText, normalizedText, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public long CaptureInFlightWatermark(string liveChatId)
    {
        if (string.IsNullOrWhiteSpace(liveChatId))
        {
            return 0;
        }

        lock (_sync)
        {
            long maxSeq = 0;

            foreach (var entry in _inFlight)
            {
                if (string.Equals(entry.LiveChatId, liveChatId, StringComparison.Ordinal) && entry.Sequence > maxSeq)
                {
                    maxSeq = entry.Sequence;
                }
            }

            return maxSeq;
        }
    }

    public async Task WaitUntilCompletedOrMatchedAsync(
        string liveChatId,
        long watermarkSeq,
        string candidateMessageId,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(liveChatId) || watermarkSeq <= 0 || timeout <= TimeSpan.Zero)
        {
            return;
        }

        Waiter? waiter = null;

        lock (_sync)
        {
            if (_sentIds.Contains((liveChatId, candidateMessageId)) || !HasInFlightAtOrBeforeLocked(liveChatId, watermarkSeq))
            {
                return;
            }

            waiter = new(
                liveChatId,
                watermarkSeq,
                candidateMessageId,
                new(TaskCreationOptions.RunContinuationsAsynchronously));
            _waiters.Add(waiter);
        }

        using var timeoutCts = new CancellationTokenSource();
        using var timer = timeProvider.CreateTimer(
            static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            },
            timeoutCts,
            timeout,
            Timeout.InfiniteTimeSpan);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var registration = linkedCts.Token.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            waiter.Completion);

        try
        {
            await waiter.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _waiters.Remove(waiter);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void CompleteInFlight(string liveChatId, long sequence, string? confirmedMessageId)
    {
        lock (_sync)
        {
            for (var i = _inFlight.Count - 1; i >= 0; i--)
            {
                if (_inFlight[i].Sequence == sequence)
                {
                    _inFlight.RemoveAt(i);
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(confirmedMessageId))
            {
                TrackSentLocked(liveChatId, confirmedMessageId);
            }

            NotifyWaitersLocked(liveChatId, confirmedMessageId);
        }
    }

    private void TrackSentLocked(string? liveChatId, string messageId)
    {
        var key = (liveChatId, messageId);
        if (_sentIds.Add(key))
        {
            _sentOrder.Enqueue(key);
            if (_sentOrder.Count > DefaultCapacity)
            {
                _sentIds.Remove(_sentOrder.Dequeue());
            }
        }
    }

    private bool HasInFlightAtOrBeforeLocked(string liveChatId, long watermarkSeq)
    {
        foreach (var entry in _inFlight)
        {
            if (string.Equals(entry.LiveChatId, liveChatId, StringComparison.Ordinal) && entry.Sequence <= watermarkSeq)
            {
                return true;
            }
        }

        return false;
    }

    private void NotifyWaitersLocked(string? liveChatId, string? confirmedMessageId)
    {
        for (var i = _waiters.Count - 1; i >= 0; i--)
        {
            var waiter = _waiters[i];
            var matchedId = !string.IsNullOrWhiteSpace(confirmedMessageId)
                && string.Equals(waiter.LiveChatId, liveChatId, StringComparison.Ordinal)
                && string.Equals(waiter.CandidateMessageId, confirmedMessageId, StringComparison.Ordinal);
            var completedWatermark = liveChatId is not null
                && string.Equals(waiter.LiveChatId, liveChatId, StringComparison.Ordinal)
                && !HasInFlightAtOrBeforeLocked(waiter.LiveChatId, waiter.WatermarkSeq);

            if (matchedId || completedWatermark)
            {
                _waiters.RemoveAt(i);
                waiter.Completion.TrySetResult();
            }
        }
    }

    private readonly record struct InFlightEntry(string LiveChatId, long Sequence, string? NormalizedText);

    private sealed record Waiter(
        string LiveChatId,
        long WatermarkSeq,
        string CandidateMessageId,
        TaskCompletionSource Completion);
}
