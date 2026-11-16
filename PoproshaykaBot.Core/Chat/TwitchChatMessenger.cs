using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public sealed class TwitchChatMessenger(
    ChatSender chatSender,
    AccountsStore accountsStore,
    ILogger<TwitchChatMessenger> logger) : IPlatformMessenger
{
    private int _whisperScopeMissingReported;

    public ChatEnqueueResult Send(
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None)
    {
        return chatSender.Enqueue(text, null, commandResponse);
    }

    public void Reply(
        string replyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None)
    {
        _ = chatSender.EnqueueAsync(text, replyToMessageId, commandResponse, CancellationToken.None);
    }

    public void Whisper(
        string toUserId,
        string fallbackReplyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None)
    {
        if (TwitchScopes.IsGranted(accountsStore.LoadBot(), TwitchScopes.UserManageWhispers))
        {
            Volatile.Write(ref _whisperScopeMissingReported, 0);
            _ = chatSender.EnqueueWhisperAsync(text, toUserId, fallbackReplyToMessageId, commandResponse, CancellationToken.None);
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

        Reply(fallbackReplyToMessageId, callerDisplayName, text, commandResponse);
    }
}
