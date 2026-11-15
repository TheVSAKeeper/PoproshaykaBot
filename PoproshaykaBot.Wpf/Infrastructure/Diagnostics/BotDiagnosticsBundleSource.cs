using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using System.Text;

namespace PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

public sealed class BotDiagnosticsBundleSource(
    IEnumerable<ISettingsSnapshotContributor> snapshotContributors,
    DiagnosticsSnapshotSource snapshots,
    ILogger<BotDiagnosticsBundleSource> logger) : IDiagnosticsBundleSource
{
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

        AppendMemory(builder, snapshot.Memory);
        AppendConnections(builder, snapshot);
        AppendQueueAndJobs(builder, snapshot);
        AppendBus(builder, snapshot.Bus);

        return builder.ToString();
    }
}
