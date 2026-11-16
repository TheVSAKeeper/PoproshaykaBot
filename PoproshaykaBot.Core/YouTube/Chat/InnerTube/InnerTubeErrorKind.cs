namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

public enum InnerTubeErrorKind
{
    None = 0,
    Unknown = 1,
    RateLimited = 2,
    Forbidden = 3,
    ConsentRequired = 4,
    LoginRequired = 5,
    MembersOnly = 6,
    StreamUnavailable = 7,
    LiveStreamEnded = 8,
    ContinuationInvalidated = 9,
    ParseFailure = 10,
    TransientNetwork = 11,
}
