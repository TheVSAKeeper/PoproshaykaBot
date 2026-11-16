namespace PoproshaykaBot.Core.YouTube.Chat;

public interface IYouTubeSentMessageTracker
{
    bool Contains(string messageId);

    void TrackSent(string messageId);

    YouTubeInFlightSendScope BeginInFlightSend(string liveChatId);

    long CaptureInFlightWatermark(string liveChatId);

    Task WaitUntilCompletedOrMatchedAsync(
        string liveChatId,
        long watermarkSeq,
        string candidateMessageId,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken);
}

public sealed class YouTubeInFlightSendScope : IDisposable
{
    private readonly YouTubeSentMessageTracker? _tracker;
    private readonly string _liveChatId;
    private readonly long _sequence;
    private int _completed;

    internal YouTubeInFlightSendScope()
    {
        _liveChatId = string.Empty;
        _completed = 1;
    }

    internal YouTubeInFlightSendScope(YouTubeSentMessageTracker tracker, string liveChatId, long sequence)
    {
        _tracker = tracker;
        _liveChatId = liveChatId;
        _sequence = sequence;
    }

    public void CompleteConfirmed(string messageId)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0 && _tracker is not null)
        {
            _tracker.CompleteInFlight(_liveChatId, _sequence, messageId);
        }
    }

    public void CompleteRejected()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0 && _tracker is not null)
        {
            _tracker.CompleteInFlight(_liveChatId, _sequence, confirmedMessageId: null);
        }
    }

    public void CompleteUncertain()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0 && _tracker is not null)
        {
            _tracker.CompleteInFlight(_liveChatId, _sequence, confirmedMessageId: null);
        }
    }

    public void Dispose()
    {
        CompleteUncertain();
    }
}

internal sealed class NullYouTubeSentMessageTracker : IYouTubeSentMessageTracker
{
    public static readonly NullYouTubeSentMessageTracker Instance = new();

    private NullYouTubeSentMessageTracker()
    {
    }

    public bool Contains(string messageId) => false;

    public void TrackSent(string messageId)
    {
    }

    public YouTubeInFlightSendScope BeginInFlightSend(string liveChatId) => new();

    public long CaptureInFlightWatermark(string liveChatId) => 0;

    public Task WaitUntilCompletedOrMatchedAsync(
        string liveChatId,
        long watermarkSeq,
        string candidateMessageId,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
