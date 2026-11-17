using KeepShell.Diagnostics;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.ViewModels.Diagnostics;

public sealed class QueueDiagnosticsCardViewModel(DiagnosticsSnapshotPublisher publisher)
    : DiagnosticsCardViewModel(publisher)
{
    private const double WarningShare = 0.8;

    private const int ErrorTextMax = 160;

    private static readonly DiagnosticsCardContent EmptyQueueContent = new(
        DiagnosticsCardState.Unknown,
        [
            new("Очередь чата", "нет данных")
            {
                Hint = "Ни очередь отправки, ни таймеры не ответили на опрос",
            },
        ]);

    private static readonly DiagnosticsCardRow[] NoChatQueueRows =
    [
        new("Очередь чата", "нет данных"),
    ];

    private readonly Dictionary<ScheduledJob, CachedJobRows> _cachedJobs = [];
    private readonly List<ScheduledJob> _cachedJobOrder = [];
    private bool _hasCachedQueue;
    private ChatQueueStatus? _cachedQueue;
    private (int Kind, long Value) _cachedQueueSentBucket;
    private DiagnosticsCardState _cachedQueueState;
    private DiagnosticsCardRow[]? _cachedQueueRows;
    private DiagnosticsCardContent? _cachedContent;

    public override string Title => "Очередь и таймеры";

    protected override DiagnosticsCardContent Build(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.ChatQueue is null && snapshot.Jobs.Count == 0)
        {
            _hasCachedQueue = false;
            _cachedQueue = null;
            _cachedQueueRows = null;
            _cachedJobs.Clear();
            _cachedJobOrder.Clear();
            _cachedContent = null;

            return EmptyQueueContent;
        }

        var (state, queueRows, queueChanged) = ResolveQueue(snapshot.ChatQueue, snapshot.CapturedAt);
        var jobsChanged = snapshot.Jobs.Count != _cachedJobOrder.Count;
        var hasError = false;

        for (var index = 0; index < snapshot.Jobs.Count; index++)
        {
            var job = snapshot.Jobs[index];
            if (ResolveJob(job, snapshot.CapturedAt)
                || index >= _cachedJobOrder.Count
                || _cachedJobOrder[index] != job.Job)
            {
                jobsChanged = true;
            }

            if (job.LastError is not null)
            {
                hasError = true;
            }
        }

        if (hasError)
        {
            state = DiagnosticsCardState.Error;
        }

        if (!queueChanged && !jobsChanged && _cachedContent is { } cached && cached.State == state)
        {
            return cached;
        }

        var rows = new List<DiagnosticsCardRow>(queueRows.Length + (snapshot.Jobs.Count * 2));
        rows.AddRange(queueRows);
        _cachedJobOrder.Clear();

        foreach (var job in snapshot.Jobs)
        {
            _cachedJobOrder.Add(job.Job);
            var entry = _cachedJobs[job.Job];
            rows.Add(entry.MainRow);
            if (entry.ErrorRow is { } errorRow)
            {
                rows.Add(errorRow);
            }
        }

        if (_cachedJobs.Count > _cachedJobOrder.Count)
        {
            foreach (var key in _cachedJobs.Keys.Where(key => !_cachedJobOrder.Contains(key)).ToArray())
            {
                _cachedJobs.Remove(key);
            }
        }

        var content = new DiagnosticsCardContent(state, rows);
        _cachedContent = content;
        return content;
    }

    private (DiagnosticsCardState State, DiagnosticsCardRow[] Rows, bool Changed) ResolveQueue(ChatQueueStatus? queueStatus, DateTimeOffset now)
    {
        var sentBucket = queueStatus is not null
            ? DiagnosticsFormat.GetAgoBucket(queueStatus.LastSentAt, now)
            : (0, 0L);

        if (_hasCachedQueue
            && _cachedQueueRows is not null
            && Equals(_cachedQueue, queueStatus)
            && _cachedQueueSentBucket == sentBucket)
        {
            return (_cachedQueueState, _cachedQueueRows, false);
        }

        DiagnosticsCardState state;
        DiagnosticsCardRow[] rows;

        if (queueStatus is not { } queue)
        {
            state = DiagnosticsCardState.Unknown;
            rows = NoChatQueueRows;
        }
        else
        {
            var share = DiagnosticsFormat.Share(queue.Length, queue.Capacity);
            rows =
            [
                new("Очередь чата", $"{DiagnosticsFormat.Number(queue.Length)} из {DiagnosticsFormat.Number(queue.Capacity)}")
                {
                    Hint = share is { } value
                        ? $"{DiagnosticsFormat.Percent(value)} ёмкости; на полной очереди отправка ждёт места"
                        : "Ёмкость очереди неизвестна",
                    Fill = share,
                },
                new("Отправлено", DiagnosticsFormat.Number(queue.SentCount)),
                new("Сбоев отправки", DiagnosticsFormat.Number(queue.FailedCount))
                {
                    Hint = "Сообщения, которые Twitch не принял; подробности – в журнале",
                },
                new("Последняя отправка", DiagnosticsFormat.Ago(queue.LastSentAt, now))
                {
                    Hint = DiagnosticsFormat.Moment(queue.LastSentAt),
                },
            ];

            state = share >= WarningShare || queue.FailedCount > 0
                ? DiagnosticsCardState.Warning
                : DiagnosticsCardState.Ok;
        }

        _hasCachedQueue = true;
        _cachedQueue = queueStatus;
        _cachedQueueSentBucket = sentBucket;
        _cachedQueueState = state;
        _cachedQueueRows = rows;

        return (state, rows, true);
    }

    private bool ResolveJob(ScheduledJobStatus job, DateTimeOffset now)
    {
        var untilBucket = DiagnosticsFormat.GetUntilBucket(job.NextRunAt, now);
        var agoBucket = DiagnosticsFormat.GetAgoBucket(job.LastRunAt, now);

        if (_cachedJobs.TryGetValue(job.Job, out var cached)
            && cached.NextRunAt == job.NextRunAt
            && cached.Interval == job.Interval
            && cached.LastRunAt == job.LastRunAt
            && cached.AgoBucket == agoBucket
            && cached.UntilBucket == untilBucket
            && string.Equals(cached.LastError, job.LastError, StringComparison.Ordinal))
        {
            return false;
        }

        var scheduleHint = cached is not null
            && cached.NextRunAt == job.NextRunAt
            && cached.Interval == job.Interval
            && cached.LastRunAt == job.LastRunAt
            && cached.AgoBucket == agoBucket
            ? cached.ScheduleHint
            : Schedule(job, now);

        var name = Describe(job.Job);
        var mainRow = cached is not null
            && cached.NextRunAt == job.NextRunAt
            && cached.UntilBucket == untilBucket
            && ReferenceEquals(cached.ScheduleHint, scheduleHint)
            ? cached.MainRow
            : new DiagnosticsCardRow(name, DiagnosticsFormat.Until(job.NextRunAt, now))
            {
                Hint = scheduleHint,
            };

        DiagnosticsCardRow? errorRow;
        if (job.LastError is not { } error)
        {
            errorRow = null;
        }
        else if (cached?.ErrorRow is not null && string.Equals(cached.LastError, error, StringComparison.Ordinal))
        {
            errorRow = cached.ErrorRow;
        }
        else
        {
            errorRow = new($"{name}: ошибка", Shorten(error))
            {
                Hint = error,
            };
        }

        _cachedJobs[job.Job] = new(
            job.NextRunAt,
            job.Interval,
            job.LastRunAt,
            job.LastError,
            untilBucket,
            agoBucket,
            scheduleHint,
            mainRow,
            errorRow);

        return true;
    }

    private static string Schedule(ScheduledJobStatus job, DateTimeOffset now)
    {
        var lastRun = $"последний запуск {DiagnosticsFormat.Ago(job.LastRunAt, now)}";

        return (job.NextRunAt, job.Interval) switch
        {
            (not null, { } interval) => $"в {DiagnosticsFormat.Moment(job.NextRunAt)}, интервал {DiagnosticsFormat.Duration(interval)}, {lastRun}",
            (not null, null) => $"в {DiagnosticsFormat.Moment(job.NextRunAt)}, {lastRun}",
            (null, { } interval) => $"интервал {DiagnosticsFormat.Duration(interval)}, {lastRun}",
            _ => lastRun,
        };
    }

    private sealed record CachedJobRows(
        DateTimeOffset? NextRunAt,
        TimeSpan? Interval,
        DateTimeOffset? LastRunAt,
        string? LastError,
        (int Kind, long Value) UntilBucket,
        (int Kind, long Value) AgoBucket,
        string ScheduleHint,
        DiagnosticsCardRow MainRow,
        DiagnosticsCardRow? ErrorRow);

    private static string Shorten(string error)
    {
        var text = error.ReplaceLineEndings(" ").Trim();

        return text.Length <= ErrorTextMax ? text : text[..ErrorTextMax] + "…";
    }

    private static string Describe(ScheduledJob job)
    {
        return job switch
        {
            ScheduledJob.Broadcast => "Следующая рассылка",
            ScheduledJob.StatisticsAutoSave => "Следующее автосохранение",
            _ => "Работа по расписанию",
        };
    }
}
