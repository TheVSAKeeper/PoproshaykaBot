using KeepShell.Diagnostics;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Diagnostics;

public sealed class ChatDiagnosticsCardViewModel(DiagnosticsSnapshotPublisher publisher)
    : DiagnosticsCardViewModel(publisher)
{
    public override string Title => "Чат";

    protected override DiagnosticsCardContent Build(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rows = new List<DiagnosticsCardRow>();
        var state = DiagnosticsCardState.Unknown;
        var hasState = false;

        if (snapshot.PlatformChats is { Count: > 0 } platformChats)
        {
            foreach (var entry in platformChats)
            {
                var platformState = AddChatRows(rows, entry.DisplayName, entry.Status, snapshot.CapturedAt);
                if (entry.Platform == StreamingPlatform.YouTube)
                {
                    AddYouTubeTransportRows(rows, snapshot.YouTubeChat, snapshot.YouTubeMonitoring);
                }

                state = CombineState(state, platformState, hasState);
                hasState = true;
            }

            return new(state, rows);
        }

        state = AddChatRows(rows, "Twitch", snapshot.Chat, snapshot.CapturedAt);
        if (snapshot.YouTubeChat is { } youtubeChat)
        {
            var youtubeState = AddChatRows(rows, "YouTube", youtubeChat.ToChatConnectionStatus(), snapshot.CapturedAt);
            AddYouTubeTransportRows(rows, youtubeChat, snapshot.YouTubeMonitoring);
            state = CombineState(state, youtubeState, hasExistingState: true);
        }

        return new(state, rows);
    }

    private static void AddYouTubeTransportRows(
        List<DiagnosticsCardRow> rows,
        YouTubeChatStatus? youtubeChat,
        YouTubeMonitoringStatus? youtubeMonitoring)
    {
        if (youtubeChat is not null)
        {
            var shortChat = youtubeChat.Transport switch
            {
                YouTubeChatTransport.InnerTube => "InnerTube (0 ед.)",
                YouTubeChatTransport.GrpcStream => "Data API (gRPC)",
                YouTubeChatTransport.RestPolling => "Data API (REST)",
                _ => "не подключён",
            };
            rows.Add(new("Транспорт чата YouTube", shortChat)
            {
                Hint = YouTubeQuotaStatusViewModel.FormatChatReadTransport(youtubeChat),
            });
        }

        if (youtubeMonitoring is not null)
        {
            var shortMonitor = !youtubeMonitoring.Enabled
                ? "отключён"
                : youtubeMonitoring.Transport switch
                {
                    YouTubeMonitoringTransport.InnerTubeDiscovery => "InnerTube (0 ед.)",
                    YouTubeMonitoringTransport.HybridInnerTubeAndDataApi => "InnerTube + Data API",
                    YouTubeMonitoringTransport.DataApi => "YouTube Data API",
                    _ => "не запущен",
                };
            rows.Add(new("Мониторинг YouTube", shortMonitor)
            {
                Hint = YouTubeQuotaStatusViewModel.FormatMonitoringTransport(youtubeMonitoring),
            });
        }
    }

    private static DiagnosticsCardState CombineState(
        DiagnosticsCardState current,
        DiagnosticsCardState incoming,
        bool hasExistingState)
    {
        if (!hasExistingState || current == DiagnosticsCardState.Unknown)
        {
            return incoming;
        }

        return incoming == DiagnosticsCardState.Unknown ? current : DiagnosticsFormat.Worst(current, incoming);
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
            ChatReadIssue.ChatDisabled => ("чат отключён на трансляции", DiagnosticsCardState.Warning),
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
