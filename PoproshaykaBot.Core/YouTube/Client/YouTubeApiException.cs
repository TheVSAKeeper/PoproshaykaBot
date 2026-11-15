using System.Net;

namespace PoproshaykaBot.Core.YouTube.Client;

public sealed class YouTubeApiException(
    YouTubeApiErrorKind kind,
    string message,
    string? reason = null,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public YouTubeApiErrorKind Kind { get; } = kind;

    public string? Reason { get; } = reason;

    public HttpStatusCode? StatusCode { get; } = statusCode;
}
