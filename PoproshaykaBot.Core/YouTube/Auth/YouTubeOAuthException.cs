using System.Net;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthException(
    string message,
    string? errorCode = null,
    HttpStatusCode? statusCode = null,
    bool isPermanentRejection = false,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string? ErrorCode { get; } = errorCode;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsPermanentRejection { get; } = isPermanentRejection;
}
