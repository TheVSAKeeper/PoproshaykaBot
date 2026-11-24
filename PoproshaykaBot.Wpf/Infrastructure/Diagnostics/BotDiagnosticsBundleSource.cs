using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Globalization;
using System.Text;

namespace PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

public sealed class BotDiagnosticsBundleSource(
    IEnumerable<ISettingsSnapshotContributor> snapshotContributors,
    DiagnosticsSnapshotSource snapshots,
    ILogger<BotDiagnosticsBundleSource> logger,
    PreviousRunState? previousRun = null,
    UnobservedTaskExceptionTracker? unobservedExceptions = null,
    BindingErrorSink? bindingErrors = null,
    IUpdateEnvironment? updateEnvironment = null,
    SettingsWriteGate? settingsWriteGate = null,
    UserStatisticsLoader? userStatisticsLoader = null,
    UserStatisticsRepository? userStatisticsRepository = null,
    BroadcastHistoryStore? broadcastHistoryStore = null,
    ActiveBroadcastStore? activeBroadcastStore = null,
    PollHistoryStore? pollHistoryStore = null,
    Func<string?>? webView2VersionProvider = null,
    Func<IReadOnlyList<DisplayMonitorInfo>>? displayMonitorProvider = null,
    UiSettingsWriteGuard? uiSettingsWriteGuard = null) : IDiagnosticsBundleSource
{
    private const int MaxBindingErrorLines = 30;
    private const int MaxBindingMessageLength = 240;
    private const int MaxRecentBroadcastLines = 8;

    private readonly Func<string?> _webView2VersionProvider = webView2VersionProvider ?? TryReadWebView2Version;
    private readonly Func<IReadOnlyList<DisplayMonitorInfo>> _displayMonitorProvider = displayMonitorProvider ?? DisplayMonitorProbe.Capture;

    public IEnumerable<DiagnosticsEntry> Collect()
    {
        if (Build("settings.txt", BuildSettings) is { } settingsEntry)
        {
            yield return settingsEntry;
        }

        if (Build("bot-snapshot.txt", BuildSnapshot) is { } snapshotEntry)
        {
            yield return snapshotEntry;
        }
    }

    private static void Append(StringBuilder builder, string store, string description)
    {
        builder.Append(store).Append(": ").AppendLine(description);
    }

    private static string Moment(DateTimeOffset? value)
    {
        return value is { } moment
            ? moment.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", UiCulture.Russian)
            : DiagnosticsFormat.Unset;
    }

    private static string YesNo(bool value) => value ? "да" : "нет";

    private static string Truncate(string value, int maxLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }

    private static string? TryReadWebView2Version()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private DiagnosticsEntry? Build(string name, Func<string> build)
    {
        try
        {
            var text = build();

            return string.IsNullOrWhiteSpace(text) ? null : new DiagnosticsEntry(name, text);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Запись {Entry} в пакет диагностики не собралась", name);

            return null;
        }
    }

    private string BuildSettings()
    {
        var builder = new StringBuilder();

        foreach (var contributor in snapshotContributors)
        {
            foreach (var entry in contributor.CollectSnapshots())
            {
                Append(builder, entry.Key, entry.Description);
            }
        }

        return builder.ToString();
    }

    private void AppendRuntimeEnvironment(StringBuilder builder)
    {
        var stamp = AppBuildBadge.StartupLine ?? "нет build-stamp";
        var updateKind = updateEnvironment?.Kind.ToString() ?? DiagnosticsFormat.Unset;
        var storageMode = AppPaths.IsBaseDirectoryOverridden
            ? "override"
            : AppPaths.IsPortable ? "portable" : "AppData";

        builder.AppendLine($"Сборка: версия {AppInfo.Version}, отметка {stamp}, обновления {updateKind}, хранение {storageMode}");

        AppendPreviousRun(builder);

        var webView2Version = _webView2VersionProvider();
        builder.AppendLine($"WebView2 Runtime: {webView2Version ?? "недоступен"}");

        var monitors = _displayMonitorProvider();
        builder.AppendLine($"Мониторы: {monitors.Count}");

        foreach (var monitor in monitors)
        {
            var primary = monitor.IsPrimary ? ", основной" : string.Empty;
            var dpiText = monitor.DpiX is { } dpiX && monitor.DpiY is { } dpiY
                ? $"DPI {dpiX}x{dpiY} ({(int)Math.Round(dpiX / 96.0 * 100.0)} %)"
                : "DPI недоступен";
            builder.AppendLine(
                $"  монитор {monitor.Index}{primary}: {monitor.Left},{monitor.Top} {monitor.Width}x{monitor.Height}, {dpiText}");
        }
    }

    private void AppendPreviousRun(StringBuilder builder)
    {
        if (previousRun is not { WasUnclean: true })
        {
            builder.AppendLine("Прошлый запуск: штатно");
            return;
        }

        if (previousRun.Record is not { } record)
        {
            builder.AppendLine("Прошлый запуск: нештатно (файл маркера не прочитан)");
            return;
        }

        builder.AppendLine(
            $"Прошлый запуск: нештатно (статус {record.Status}, PID {record.ProcessId}, старт {Moment(record.StartedAtUtc)}, "
            + $"обновление {Moment(record.UpdatedAtUtc)}, версия {record.Version}, коммит {record.Commit ?? DiagnosticsFormat.Unset}, "
            + $"сбой {record.ExceptionType ?? DiagnosticsFormat.Unset})");
    }

    private void AppendErrorSensors(StringBuilder builder)
    {
        if (bindingErrors is not null)
        {
            var records = bindingErrors.Records;
            builder.AppendLine($"Ошибки привязки WPF: всего {bindingErrors.Count}, в буфере {records.Count}");

            foreach (var record in records.TakeLast(MaxBindingErrorLines))
            {
                var scope = string.IsNullOrWhiteSpace(record.Case) ? string.Empty : $"[{record.Case}/{record.Theme}] ";
                builder.AppendLine($"  привязка: {scope}{Truncate(record.Message, MaxBindingMessageLength)}");
            }
        }

        if (unobservedExceptions is not null)
        {
            var snapshot = unobservedExceptions.Capture();
            builder.AppendLine($"Необработанные фоновые задачи: сбоев {snapshot.FailureCount}, отмен {snapshot.CanceledCount}");

            foreach (var failure in snapshot.RecentFailures)
            {
                var frame = string.IsNullOrWhiteSpace(failure.TopFrame) ? string.Empty : $" @ {failure.TopFrame}";
                builder.AppendLine($"  задача {Moment(failure.ObservedAtUtc)}: {failure.ExceptionType}{frame}");
            }
        }
    }

    private void AppendStorageHealth(StringBuilder builder)
    {
        if (settingsWriteGate is not null)
        {
            builder.AppendLine($"Гейт записи настроек: {(settingsWriteGate.HasRevoked ? "есть заблокированные файлы" : "свободен")}");
        }

        if (uiSettingsWriteGuard is not null)
        {
            var uiGuard = uiSettingsWriteGuard.CaptureStatus();
            var state = uiGuard.IsRevoked
                ? "заблокирован до перезапуска"
                : uiGuard.IsSuspended ? "приостановлен" : "свободен";
            builder.AppendLine($"Гейт записи настроек UI: {state}, удержано ключей {uiGuard.HeldKeyCount}");
        }

        if (userStatisticsLoader is not null)
        {
            var state = userStatisticsLoader.LoadState;
            if (state == StatisticsLoadState.Loaded && userStatisticsRepository is not null)
            {
                var (accounts, channelCounters, unattributedCounters, linkGroups) = userStatisticsRepository.GetCatalogCounts();
                builder.AppendLine(
                    $"Статистика пользователей: состояние {state}, аккаунтов {accounts}, канальных {channelCounters}, "
                    + $"без канала {unattributedCounters}, связей {linkGroups}");
            }
            else
            {
                builder.AppendLine($"Статистика пользователей: состояние {state}");
            }
        }

        if (broadcastHistoryStore is not null)
        {
            var summary = broadcastHistoryStore.CaptureDiagnosticsSummary(MaxRecentBroadcastLines);
            builder.AppendLine(
                $"История эфиров: источник {summary.SourceReadState}, проекция {summary.ProjectionReadState}, "
                + $"запись {YesNo(summary.CanWriteSource)}/{YesNo(summary.CanWriteProjection)}, "
                + $"проекция отстаёт {YesNo(summary.ProjectionOutOfDate)}, ожидает записи {YesNo(summary.HasPendingFlush)}, "
                + $"всего {summary.TotalCount}");

            foreach (var item in summary.Recent)
            {
                var platforms = item.Platforms.Count == 0
                    ? DiagnosticsFormat.Unset
                    : string.Join('+', item.Platforms);
                var tracked = item.TrackedDuration is { } duration
                    ? FormatDuration(duration)
                    : DiagnosticsFormat.Unset;
                var shortId = item.Id.ToString("N", CultureInfo.InvariantCulture)[..8];

                builder.AppendLine(
                    $"  эфир {shortId}: площадки {platforms}, канал {item.PrimaryChannelId ?? DiagnosticsFormat.Unset}, "
                    + $"{Moment(item.StartedAt)}..{Moment(item.EndedAt)}, сегментов {item.SegmentCount} ({FormatDuration(item.SegmentsTotalDuration)}), "
                    + $"учтено {tracked}, скрыт {YesNo(item.IsHidden)}");
            }
        }

        if (activeBroadcastStore is not null)
        {
            var active = activeBroadcastStore.CaptureDiagnosticsSummary();
            if (!active.HasDraft)
            {
                builder.AppendLine($"Активный черновик эфира: чтение {active.ReadState}, запись {YesNo(active.CanWrite)}, черновика нет");
            }
            else
            {
                var platforms = active.Platforms.Count == 0
                    ? DiagnosticsFormat.Unset
                    : string.Join(", ", active.Platforms.Select(static p =>
                        $"{p.Platform}({p.ChannelId ?? DiagnosticsFormat.Unset}/{p.StreamId ?? DiagnosticsFormat.Unset})"));
                var shortId = active.BroadcastId?.ToString("N", CultureInfo.InvariantCulture)[..8] ?? DiagnosticsFormat.Unset;

                builder.AppendLine(
                    $"Активный черновик эфира: чтение {active.ReadState}, запись {YesNo(active.CanWrite)}, "
                    + $"id {shortId}, старт {Moment(active.StartedAt)}, обновлён {Moment(active.UpdatedAt)}, "
                    + $"завершён {Moment(active.EndedAt)}, площадки {platforms}");
            }
        }

        if (pollHistoryStore is not null)
        {
            var polls = pollHistoryStore.CaptureDiagnosticsSummary();
            builder.AppendLine(
                $"История голосований: чтение {polls.ReadState}, запись {YesNo(polls.CanPersist)}, "
                + $"переписана снаружи {YesNo(polls.RewrittenExternally)}, несохранённых {YesNo(polls.HasUnsavedChanges)}, "
                + $"записей {polls.EntryCount}");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration < TimeSpan.Zero
            ? "00:00:00"
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
    }

    private static void AppendMemory(StringBuilder builder, MemoryUsage? usage)
    {
        if (usage is not { } memory)
        {
            builder.AppendLine("Память: данных нет");

            return;
        }

        builder.AppendLine($"Память: приложение {memory.SelfBytes} B из {memory.SelfThresholdBytes} B, "
            + $"дочерние {memory.ChildBytes} B в {memory.ChildCount} проц. из {memory.TotalThresholdBytes} B, замер {Moment(memory.MeasuredAt)}");

        foreach (var child in memory.Children)
        {
            builder.AppendLine($"  процесс {child.Name}: {child.Bytes} B, {child.Count} шт.");
        }
    }

    private static void AppendConnections(StringBuilder builder, DiagnosticsSnapshot snapshot)
    {
        foreach (var session in snapshot.EventSub)
        {
            builder.AppendLine($"EventSub {session.Role}: {session.State}, последнее сообщение {Moment(session.LastMessageAt)}, "
                + $"попытка {session.ReconnectAttempt} из {session.ReconnectLimit}");
        }

        builder.AppendLine(snapshot.Chat is { } chat
            ? $"Чат: {chat.State}, канал {chat.Channel ?? DiagnosticsFormat.Unset}, вход {Moment(chat.JoinedAt)}, последнее сообщение {Moment(chat.LastMessageAt)}"
            : "Чат: данных нет");

        builder.AppendLine(snapshot.Obs is { } obs
            ? $"OBS: {obs.State}, последний обмен {Moment(obs.LastEventAt)}"
            : "OBS: данных нет");

        if (snapshot.Sse is { } sse)
        {
            var running = sse.Running ? "работает" : "остановлен";

            builder.AppendLine($"Веб-сервер: {running}, клиентов {sse.ClientCount}, отброшено {sse.DroppedMessageCount}");
        }
        else
        {
            builder.AppendLine("Веб-сервер: данных нет");
        }

        AppendYouTubeSection(builder, snapshot);
    }

    private static void AppendYouTubeSection(StringBuilder builder, DiagnosticsSnapshot snapshot)
    {
        if (snapshot.YouTubeMonitoring is { } ytMonitor)
        {
            var streamText = !ytMonitor.Enabled
                ? "отключён"
                : ytMonitor.StreamStatus switch
                {
                    StreamStatus.Online => "онлайн",
                    StreamStatus.Offline => "офлайн",
                    _ => "неизвестно",
                };

            var issueText = ytMonitor.Issue switch
            {
                YouTubeMonitorIssue.MissingBroadcasterAuth => "нужна авторизация ведущего",
                YouTubeMonitorIssue.MissingBotAuth => "нужна авторизация бота",
                YouTubeMonitorIssue.InvalidTargetChannel => "целевой канал не найден",
                YouTubeMonitorIssue.MultipleActiveBroadcasts => $"несколько активных трансляций ({ytMonitor.ActiveCandidateCount})",
                YouTubeMonitorIssue.QuotaExhausted => "квота исчерпана",
                YouTubeMonitorIssue.TransientError => $"ошибка опроса (сбоев: {ytMonitor.ConsecutiveFailures})",
                YouTubeMonitorIssue.AccessUnverified => "доступ аккаунта не подтверждён",
                _ => "без замечаний",
            };

            var transportText = ytMonitor.Transport switch
            {
                YouTubeMonitoringTransport.InnerTubeDiscovery => "InnerTube",
                YouTubeMonitoringTransport.HybridInnerTubeAndDataApi => "InnerTube + Data API",
                YouTubeMonitoringTransport.DataApi => "Data API",
                _ => "нет",
            };

            builder.AppendLine(
                $"Мониторинг YouTube: {streamText}, канал {ytMonitor.TargetChannelId ?? DiagnosticsFormat.Unset}, "
                + $"видео {ytMonitor.ActiveVideoId ?? DiagnosticsFormat.Unset}, чат {ytMonitor.ActiveLiveChatId ?? DiagnosticsFormat.Unset}, "
                + $"состояние: {issueText}, последний опрос {Moment(ytMonitor.LastPollAt)}, транспорт {transportText}");
        }
        else
        {
            builder.AppendLine("Мониторинг YouTube: данных нет");
        }

        if (snapshot.YouTubeChat is { } ytChat)
        {
            var transportText = ytChat.Transport switch
            {
                YouTubeChatTransport.GrpcStream => "gRPC",
                YouTubeChatTransport.RestPolling => "REST",
                YouTubeChatTransport.InnerTube => "InnerTube",
                _ => "нет",
            };

            var issueSuffix = ytChat.Issue switch
            {
                ChatReadIssue.MissingBotAuth => ", нужна авторизация бота",
                ChatReadIssue.WaitingForActiveBroadcast => ", ожидание активной трансляции",
                ChatReadIssue.ChatDisabled => ", чат отключён на трансляции",
                ChatReadIssue.QuotaExhausted => ", квота исчерпана",
                ChatReadIssue.TransientError => ", ошибка чтения",
                _ => string.Empty,
            };

            builder.AppendLine(
                $"Чат YouTube: {DiagnosticsFormat.Describe(ytChat.State)} ({transportText}{issueSuffix}), "
                + $"канал {ytChat.Channel ?? DiagnosticsFormat.Unset}, чат {ytChat.LiveChatId ?? DiagnosticsFormat.Unset}, "
                + $"сообщений {DiagnosticsFormat.Number(ytChat.ReceivedMessages)}, разрывов курсора {ytChat.GapCount}, "
                + $"последнее сообщение {Moment(ytChat.LastMessageAt)}");
        }
        else
        {
            builder.AppendLine("Чат YouTube: данных нет");
        }

        if (snapshot.YouTubeQuota is { } ytQuota)
        {
            var markers = new List<string>(3);

            if (ytQuota.ExhaustedByServer)
            {
                markers.Add("исчерпана по ответу сервера");
            }

            if (ytQuota.Reliability == YouTubeQuotaReliability.CorruptFile)
            {
                markers.Add("файл quota.json повреждён");
            }

            if (!ytQuota.CanWriteFile)
            {
                markers.Add("запись заморожена до перезапуска");
            }

            var markerSuffix = markers.Count > 0 ? $", {string.Join(", ", markers)}" : string.Empty;

            var categorySummary = ytQuota.CategoryUnits is { Count: > 0 }
                ? ", категории: " + string.Join(", ", ytQuota.CategoryUnits.OrderBy(static kv => (int)kv.Key).Select(static kv => $"{kv.Key}={kv.Value}"))
                : string.Empty;

            builder.AppendLine(
                $"Квота YouTube ({ytQuota.ProjectKey ?? DiagnosticsFormat.Unset}): "
                + $"{DiagnosticsFormat.Number(ytQuota.CommittedUnits)} из {DiagnosticsFormat.Number(ytQuota.DailyBudget)} "
                + $"(в резерве {DiagnosticsFormat.Number(ytQuota.ReservedUnits)}, "
                + $"бот {DiagnosticsFormat.Number(ytQuota.BotCommittedUnits)}, "
                + $"ведущий {DiagnosticsFormat.Number(ytQuota.BroadcasterCommittedUnits)}, "
                + $"сброс {Moment(ytQuota.NextResetAt)}{categorySummary}{markerSuffix})");
        }
        else
        {
            builder.AppendLine("Квота YouTube: данных нет");
        }
    }

    private static void AppendQueueAndJobs(StringBuilder builder, DiagnosticsSnapshot snapshot)
    {
        builder.AppendLine(snapshot.ChatQueue is { } queue
            ? $"Очередь чата: {queue.Length} из {queue.Capacity}, отправлено {queue.SentCount}, сбоев {queue.FailedCount}, последняя отправка {Moment(queue.LastSentAt)}"
            : "Очередь чата: данных нет");

        foreach (var job in snapshot.Jobs)
        {
            builder.AppendLine($"Работа {job.Job}: интервал {job.Interval?.ToString() ?? DiagnosticsFormat.Unset}, "
                + $"следующий запуск {Moment(job.NextRunAt)}, последний {Moment(job.LastRunAt)}, ошибка {job.LastError ?? DiagnosticsFormat.Unset}");
        }
    }

    private static void AppendBus(StringBuilder builder, EventBusStatistics? statistics)
    {
        if (statistics is not { } bus)
        {
            builder.AppendLine("Шина: данных нет");

            return;
        }

        builder.AppendLine($"Шина: публикаций {bus.PublishedTotal}, сбоев обработчиков {bus.HandlerFailures}, "
            + $"продолжений запущено {bus.ContinuationsStarted}, упало {bus.ContinuationFailures}");

        foreach (var type in bus.ByType)
        {
            builder.AppendLine($"  {type.EventType}: публикаций {type.PublishCount}, сбоев {type.HandlerFailureCount}, "
                + $"суммарно {type.TotalDuration}, максимум {type.MaxDuration}, последняя {Moment(type.LastPublishedAt)}");
        }
    }

    private string BuildSnapshot()
    {
        var snapshot = snapshots.Capture();
        var builder = new StringBuilder();

        builder.Append("Снимок: ").AppendLine(Moment(snapshot.CapturedAt));

        AppendRuntimeEnvironment(builder);
        AppendErrorSensors(builder);
        AppendStorageHealth(builder);
        AppendMemory(builder, snapshot.Memory);
        AppendConnections(builder, snapshot);
        AppendQueueAndJobs(builder, snapshot);
        AppendBus(builder, snapshot.Bus);

        return builder.ToString();
    }
}
