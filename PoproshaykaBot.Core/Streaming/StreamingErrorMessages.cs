using PoproshaykaBot.Core.Twitch.Helix;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace PoproshaykaBot.Core.Streaming;

internal static class StreamingErrorMessages
{
    public static string SafeMessage(Exception exception)
    {
        return exception switch
        {
            WebSocketException or SocketException => "соединение с EventSub прервано",
            JsonException => "некорректный ответ Twitch",
            _ => HelixErrorMessages.SafeMessage(exception),
        };
    }
}
