using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public sealed class TwitchChatMessenger(
    ChatSender chatSender,
    AccountsStore accountsStore,
    ILogger<TwitchChatMessenger> logger,
    TwitchChannelAuthorization? authorization = null) : IPlatformMessenger
{
    private int _whisperScopeMissingReported;

    public ChatEnqueueResult Send(
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        return chatSender.Enqueue(text, null, commandResponse, origin);
    }

    public void Reply(
        string replyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        _ = chatSender.Enqueue(text, replyToMessageId, commandResponse, origin);
    }

    public TrackedChatSend SendTracked(string text, ChatSendCategory category) => chatSender.EnqueueTracked(text);

    public void Whisper(
        string toUserId,
        string fallbackReplyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        var bot = authorization is null ? accountsStore.LoadBot() : authorization.GetAccount(PlatformAuthRole.Bot);
        if (bot is not null && TwitchScopes.IsGranted(bot, TwitchScopes.UserManageWhispers))
        {
            Volatile.Write(ref _whisperScopeMissingReported, 0);
            _ = chatSender.EnqueueWhisperAsync(text, toUserId, fallbackReplyToMessageId, commandResponse, CancellationToken.None, origin);
            return;
        }

        if (Interlocked.Exchange(ref _whisperScopeMissingReported, 1) == 0)
        {
            logger.LogWarning("Ответ команды {Canonical} не отправлен шёпотом: у бота нет права {Scope} (необязательное, включается повторной авторизацией бота) – ответ уходит реплаем на сообщение вызвавшего, повторы этой строки пишутся в Debug",
                commandResponse?.Canonical ?? string.Empty,
                TwitchScopes.UserManageWhispers);
        }
        else
        {
            logger.LogDebug("Ответ команды {Canonical} уходит реплаем вместо шёпота: у бота нет права {Scope}",
                commandResponse?.Canonical ?? string.Empty,
                TwitchScopes.UserManageWhispers);
        }

        Reply(fallbackReplyToMessageId, callerDisplayName, text, commandResponse, category, origin);
    }
}
