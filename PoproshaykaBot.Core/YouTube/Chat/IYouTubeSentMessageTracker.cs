namespace PoproshaykaBot.Core.YouTube.Chat;

public interface IYouTubeSentMessageTracker
{
    bool Contains(string messageId);

    void TrackSent(string messageId);
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
}
