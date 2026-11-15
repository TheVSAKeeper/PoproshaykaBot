namespace PoproshaykaBot.Core.YouTube.Client;

public enum YouTubeApiErrorKind
{
    None = 0,
    Unknown = 1,
    Unauthorized = 2,
    Forbidden = 3,
    QuotaExceeded = 4,
    RateLimitExceeded = 5,
    InvalidPageToken = 6,
    LiveChatEnded = 7,
    BadRequest = 8,
    NotFound = 9,
    TransportUnsupported = 10,
    Transient = 11,
    LocalQuotaBudgetReached = 12,
}
