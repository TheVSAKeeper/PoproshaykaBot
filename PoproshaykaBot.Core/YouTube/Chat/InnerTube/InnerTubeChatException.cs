using System.Net;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

public sealed class InnerTubeChatException(
    InnerTubeErrorKind kind,
    string message,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public InnerTubeErrorKind Kind { get; } = kind;

    public HttpStatusCode? StatusCode { get; } = statusCode;
}
