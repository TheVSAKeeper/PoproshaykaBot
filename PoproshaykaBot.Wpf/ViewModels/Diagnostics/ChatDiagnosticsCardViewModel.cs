using KeepShell.Diagnostics;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.ViewModels.Diagnostics;

public sealed class ChatDiagnosticsCardViewModel(DiagnosticsSnapshotPublisher publisher)
    : DiagnosticsCardViewModel(publisher)
{
    public override string Title => "Чат";

    protected override DiagnosticsCardContent Build(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rows = new List<DiagnosticsCardRow>();
        var state = AddChatRows(rows, "Twitch", snapshot.Chat, snapshot.CapturedAt);
        if (snapshot.YouTubeChat is { } youtubeChat)
        {
            var youtubeState = AddChatRows(rows, "YouTube", youtubeChat.ToChatConnectionStatus(), snapshot.CapturedAt);
            state = state == DiagnosticsCardState.Unknown
                ? youtubeState
                : youtubeState == DiagnosticsCardState.Unknown ? state : DiagnosticsFormat.Worst(state, youtubeState);
        }

        return new(state, rows);
    }

    private static DiagnosticsCardState AddChatRows(
        List<DiagnosticsCardRow> rows, string platform, ChatConnectionStatus? chat, DateTimeOffset capturedAt)
    {
        if (chat is null)
        {
            rows.Add(new(platform, "нет данных") { Hint = "Источник состояния чата не ответил на опрос" });
            return DiagnosticsCardState.Unknown;
        }

        var (status, state) = chat.Issue switch
        {
            ChatReadIssue.MissingBotAuth => ("чат не читается: токен бота недоступен", DiagnosticsCardState.Error),
            ChatReadIssue.WaitingForActiveBroadcast => ("ожидание чата эфира", DiagnosticsCardState.Warning),
            ChatReadIssue.QuotaExhausted => ("чат не читается: квота исчерпана", DiagnosticsCardState.Warning),
            ChatReadIssue.TransientError => ("сбой чтения чата, повтор подключения", DiagnosticsCardState.Warning),
            ChatReadIssue.SubscriptionFailed => ("подписка на чат не подтверждена", DiagnosticsCardState.Error),
            _ => (DiagnosticsFormat.Describe(chat.State), DiagnosticsFormat.ToCardState(chat.State)),
        };

        rows.AddRange(
        [
            new(platform, status),
            new($"Канал {platform}", chat.Channel ?? DiagnosticsFormat.Unset),
            new($"В канале {platform} с", DiagnosticsFormat.Ago(chat.JoinedAt, capturedAt))
            {
                Hint = DiagnosticsFormat.Moment(chat.JoinedAt),
            },
            new($"Последнее сообщение {platform}", DiagnosticsFormat.Ago(chat.LastMessageAt, capturedAt))
            {
                Hint = DiagnosticsFormat.Moment(chat.LastMessageAt),
            },
        ]);
        return state;
    }
}
