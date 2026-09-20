using KeepShell.Diagnostics;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.ViewModels.Diagnostics;

public sealed class EventBusDiagnosticsCardViewModel(DiagnosticsSnapshotPublisher publisher)
    : DiagnosticsCardViewModel(publisher)
{
    private const int TypeRows = 10;

    public override string Title => "Шина событий";

    protected override DiagnosticsCardContent Build(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Bus is not { } bus)
        {
            return new(DiagnosticsCardState.Unknown,
            [
                new("Публикаций", "нет данных")
                {
                    Hint = "Счётчики шины не ответили на опрос",
                },
            ]);
        }

        var rows = new List<DiagnosticsCardRow>
        {
            new("Публикаций", DiagnosticsFormat.Number(bus.PublishedTotal))
            {
                Hint = "Считаются и события без единого подписчика",
            },
            new("Сбоев обработчиков", DiagnosticsFormat.Number(bus.HandlerFailures))
            {
                Hint = "Обработчик бросил исключение; соседние обработчики того же события при этом отработали",
            },
            new("Продолжения", $"запущено {DiagnosticsFormat.Number(bus.ContinuationsStarted)}, упало {DiagnosticsFormat.Number(bus.ContinuationFailures)}")
            {
                Hint = "Работа, поставленная через ContinueAfterPublish: она идёт после всех обработчиков публикации",
            },
        };

        foreach (var type in bus.ByType.Take(TypeRows))
        {
            rows.Add(new(type.EventType,
                $"{DiagnosticsFormat.Number(type.PublishCount)} · сбоев {DiagnosticsFormat.Number(type.HandlerFailureCount)} · макс. {DiagnosticsFormat.Milliseconds(type.MaxDuration)}")
            {
                Hint = $"последняя публикация {DiagnosticsFormat.Ago(type.LastPublishedAt, snapshot.CapturedAt)}, "
                    + $"суммарно {DiagnosticsFormat.Milliseconds(type.TotalDuration)}",
            });
        }

        var state = bus.HandlerFailures > 0 || bus.ContinuationFailures > 0
            ? DiagnosticsCardState.Warning
            : DiagnosticsCardState.Ok;

        return new(state, rows);
    }
}
