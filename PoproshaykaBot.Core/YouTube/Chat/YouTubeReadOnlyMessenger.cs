using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class YouTubeReadOnlyMessenger(ILogger<YouTubeReadOnlyMessenger> logger) : IPlatformMessenger
{
    public ChatEnqueueResult Send(string text, CommandResponseMark? commandResponse = null)
    {
        logger.LogDebug("Отправка в чат YouTube пропущена: этап 6 работает в режиме только чтения.");
        return ChatEnqueueResult.ObserverMode;
    }

    public void Reply(
        string replyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null)
    {
        logger.LogDebug("Ответ в чат YouTube пропущен: этап 6 работает в режиме только чтения.");
    }

    public void Whisper(
        string toUserId,
        string fallbackReplyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null)
    {
        logger.LogDebug("Личное сообщение YouTube пропущено: этап 6 работает в режиме только чтения.");
    }
}
