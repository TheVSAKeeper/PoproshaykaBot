using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels.Diagnostics;

namespace PoproshaykaBot.Wpf.Tests.Diagnostics;

[TestFixture]
public class DiagnosticsCardTests
{
    [Test]
    public void Память_без_сторожа_это_нет_данных_а_не_ноль()
    {
        var card = Activate(publisher => new MemoryDiagnosticsCardViewModel(publisher, Monitor()), DiagnosticsSnapshots.Empty());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Unknown), "Под --ui-smoke сторожа памяти нет вовсе, и это не «всё в порядке»");
            Assert.That(card.Rows, Has.Count.EqualTo(1));
            Assert.That(card.Rows[0].Hint, Does.Contain("Сторож памяти не запущен"), "Пустая карточка обязана объяснить, почему чисел нет");
            Assert.That(card.Rows[0].Fill, Is.Null, "Полосы без числа быть не должно");
        }
    }

    [TestCase(0.5, DiagnosticsCardState.Ok)]
    [TestCase(0.85, DiagnosticsCardState.Warning)]
    [TestCase(1.0, DiagnosticsCardState.Warning)]
    [TestCase(1.2, DiagnosticsCardState.Error)]
    public void Ровно_порог_памяти_отличим_от_превышения(double share, DiagnosticsCardState expected)
    {
        var selfBytes = (long)(DiagnosticsSnapshots.Gigabyte * share);
        var snapshot = DiagnosticsSnapshots.Empty() with { Memory = DiagnosticsSnapshots.Memory(selfBytes, 0) };
        var card = Activate(publisher => new MemoryDiagnosticsCardViewModel(publisher, Monitor()), snapshot);
        var row = card.Rows[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(expected));
            Assert.That(row.Fill, Is.EqualTo(share).Within(0.01), "Доля шкалы считается от порога сторожа");
            Assert.That(row.Value, Does.Contain("из 1 ГБ"), "Полоса зажимается каркасом, поэтому превышение обязан нести текст значения");
        }
    }

    [Test]
    public void Полоса_дублирует_число_которое_стоит_в_значении()
    {
        var snapshot = DiagnosticsSnapshots.Full() with { ChatQueue = new(800, 1000, 120, 0, DiagnosticsSnapshots.CapturedAt) };
        var card = Activate(publisher => new QueueDiagnosticsCardViewModel(publisher), snapshot);
        var row = card.Rows[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Value,
                Is.EqualTo($"{DiagnosticsFormat.Number(800)} из {DiagnosticsFormat.Number(1000)}"),
                "Скринридеру полоса не видна – число обязано стоять в значении");
            Assert.That(row.Fill, Is.EqualTo(0.8).Within(0.001));
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Warning), "Очередь на 80 % ёмкости – это уже предупреждение");
        }
    }

    [Test]
    public void Сорвавшаяся_работа_по_расписанию_красит_карточку_ошибкой()
    {
        var snapshot = DiagnosticsSnapshots.Full() with
        {
            Jobs = [new(ScheduledJob.StatisticsAutoSave, TimeSpan.FromMinutes(1), null, DiagnosticsSnapshots.CapturedAt, "диск недоступен")],
        };

        var card = Activate(publisher => new QueueDiagnosticsCardViewModel(publisher), snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Error));
            Assert.That(card.Rows.Any(row => row.Value.Contains("диск недоступен", StringComparison.Ordinal)), Is.True);
        }
    }

    [TestCase(ConnectionState.Disconnected, DiagnosticsCardState.Unknown)]
    [TestCase(ConnectionState.Unknown, DiagnosticsCardState.Unknown)]
    [TestCase(ConnectionState.Connecting, DiagnosticsCardState.Warning)]
    [TestCase(ConnectionState.Connected, DiagnosticsCardState.Ok)]
    [TestCase(ConnectionState.Failed, DiagnosticsCardState.Error)]
    public void Выключенный_бот_это_нет_данных_а_не_ошибка(ConnectionState state, DiagnosticsCardState expected)
    {
        var snapshot = DiagnosticsSnapshots.Full() with { Chat = new(state, "bobito217", null, null) };
        var card = Activate(publisher => new ChatDiagnosticsCardViewModel(publisher), snapshot);

        Assert.That(card.State, Is.EqualTo(expected));
    }

    [Test]
    public void Чат_показывает_отказ_YouTube_при_подключённом_Twitch()
    {
        var snapshot = DiagnosticsSnapshots.Full() with
        {
            YouTubeChat = new(ConnectionState.Reconnecting, YouTubeChatTransport.GrpcStream, ChatReadIssue.MissingBotAuth,
                "@VissGames", "chat", null, null, null, 0, 0, 1),
        };
        var card = Activate(publisher => new ChatDiagnosticsCardViewModel(publisher), snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Error));
            Assert.That(card.Rows.Single(row => row.Key == "Twitch").Value, Is.EqualTo("подключено"));
            Assert.That(card.Rows.Single(row => row.Key == "YouTube").Value, Does.Contain("токен бота недоступен"));
        }
    }

    [Test]
    public void Отключённый_YouTube_не_меняет_состояние_работающего_чата_Twitch()
    {
        var snapshot = DiagnosticsSnapshots.Full() with
        {
            YouTubeChat = new(ConnectionState.Disconnected, YouTubeChatTransport.None, ChatReadIssue.None,
                null, null, null, null, null, 0, 0, 0),
        };
        var card = Activate(publisher => new ChatDiagnosticsCardViewModel(publisher), snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Ok));
            Assert.That(card.Rows.Single(row => row.Key == "YouTube").Value, Is.EqualTo("отключено"));
        }
    }

    [Test]
    public void Отказ_источника_соединения_виден_как_нет_данных()
    {
        var snapshot = DiagnosticsSnapshots.Full() with { Obs = null, Sse = null };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Activate(publisher => new ObsDiagnosticsCardViewModel(publisher), snapshot).State,
                Is.EqualTo(DiagnosticsCardState.Unknown));
            Assert.That(Activate(publisher => new SseDiagnosticsCardViewModel(publisher), snapshot).State,
                Is.EqualTo(DiagnosticsCardState.Unknown));
        }
    }

    [Test]
    public void Остановленный_веб_сервер_не_считается_отказом()
    {
        var snapshot = DiagnosticsSnapshots.Full() with { Sse = new(false, 0, 0) };
        var card = Activate(publisher => new SseDiagnosticsCardViewModel(publisher), snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Unknown));
            Assert.That(card.Rows[0].Value, Is.EqualTo("остановлен"));
        }
    }

    [Test]
    public void Карточка_памяти_отдаёт_каркасу_замер_дочерних_процессов()
    {
        var dispatcher = new ManualUiDispatcher();
        var snapshot = DiagnosticsSnapshots.Full();

        using var monitor = new PerformanceMonitor(new(), dispatcher, NullLogger<PerformanceMonitor>.Instance);

        monitor.Start();

        var publisher = new DiagnosticsSnapshotPublisher(dispatcher,
            () => snapshot,
            NullLogger<DiagnosticsSnapshotPublisher>.Instance);

        new MemoryDiagnosticsCardViewModel(publisher, monitor).SetActive(true);

        dispatcher.Timers[0].Tick();

        var children = monitor.Snapshot.Children;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(children, Is.Not.Null, "Без этого график каркаса о WebView2 не знает");
            Assert.That(children!.Count, Is.EqualTo(4));
            Assert.That(children.PrivateBytes, Is.EqualTo(snapshot.Memory!.ChildBytes));
        }
    }

    [Test]
    public void Счётчики_шины_идут_самыми_частыми_типами()
    {
        var card = Activate(publisher => new EventBusDiagnosticsCardViewModel(publisher), DiagnosticsSnapshots.Full());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.Rows[0].Value, Is.EqualTo(DiagnosticsFormat.Number(1200)));
            Assert.That(card.Rows.Any(row => string.Equals(row.Key, "StreamWentOnline", StringComparison.Ordinal)), Is.True);
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Ok));
        }
    }

    [Test]
    public void Показатели_типа_события_идут_колонками_под_общей_шапкой()
    {
        var card = Activate(publisher => new EventBusDiagnosticsCardViewModel(publisher), Bus(6));
        var header = card.Rows.Single(static row => row.IsHeader);
        var first = card.Rows.First(static row => row.HasColumns && !row.IsHeader);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(header.Columns, Has.Count.EqualTo(3), "Подписи колонок даются один раз на карточку");
            Assert.That(first.Columns, Has.Count.EqualTo(header.Columns.Count), "Числа обязаны встать под подписями");
            Assert.That(first.Value, Is.Empty, "Склейка чисел в одно значение – это то, от чего уходили");
            Assert.That(first.Hint, Does.Contain("сбоев обработчиков"), "Диктор читает подсказку: без неё числа остаются без подписей");
        }
    }

    [Test]
    public void Карточка_шины_показывает_первые_типы_и_разворачивается_кнопкой()
    {
        var card = Activate(publisher => new EventBusDiagnosticsCardViewModel(publisher), Bus(9));
        var collapsed = card.Rows.Count(static row => row.HasColumns && !row.IsHeader);

        Assert.That(card.Command, Is.Not.Null, "Типов больше, чем влезает – кнопка развёртки обязана быть");
        Assert.That(card.CommandCaption, Does.Contain("9"), "Подпись кнопки называет, сколько типов всего");

        card.Command!.Execute(null);

        var expanded = card.Rows.Count(static row => row.HasColumns && !row.IsHeader);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collapsed, Is.EqualTo(4), "Свёрнутая карточка не должна тянуть вверх весь ряд");
            Assert.That(expanded, Is.EqualTo(9), "Развёрнутая карточка обязана показать остальные типы");
            Assert.That(card.CommandCaption, Is.EqualTo("Показать меньше"));
        }
    }

    [Test]
    public void Подпись_кнопки_не_выдаёт_потолок_строк_за_число_типов()
    {
        var card = Activate(publisher => new EventBusDiagnosticsCardViewModel(publisher), Bus(14));

        Assert.That(card.CommandCaption, Is.EqualTo("Показать 10 самых частых из 14"),
            "Развернуться можно только до потолка строк, и подпись обязана называть оба числа");

        card.Command!.Execute(null);

        Assert.That(card.Rows.Count(static row => row.HasColumns && !row.IsHeader), Is.EqualTo(10),
            "Обещанное подписью число строк и есть то, что показывает разворот");
    }

    [Test]
    public void Дочерние_процессы_идут_колонками_а_не_склейкой()
    {
        var snapshot = DiagnosticsSnapshots.Empty() with
        {
            Memory = DiagnosticsSnapshots.Memory(DiagnosticsSnapshots.Gigabyte / 4, DiagnosticsSnapshots.Gigabyte / 2),
        };
        var card = Activate(publisher => new MemoryDiagnosticsCardViewModel(publisher, Monitor()), snapshot);
        var header = card.Rows.Single(static row => row.IsHeader);
        var child = card.Rows.First(static row => row.HasColumns && !row.IsHeader);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(header.Columns, Has.Count.EqualTo(child.Columns.Count), "Числа обязаны встать под подписями");
            Assert.That(child.Value, Is.Empty, "Склейка памяти и числа процессов через точку – это то, от чего уходили");
            Assert.That(child.Fill, Is.Not.Null, "Доля общего порога у строки процесса остаётся шкалой");
            Assert.That(child.Hint, Does.Contain("процесс"), "Диктор читает подсказку: без неё числа остаются без подписей");
        }
    }

    [Test]
    public void Шина_без_единого_типа_обходится_без_шапки_и_кнопки()
    {
        var card = Activate(publisher => new EventBusDiagnosticsCardViewModel(publisher), Bus(0));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.Rows.Any(static row => row.IsHeader), Is.False, "Шапка колонок без единой строки чисел – пустая секция с заголовком");
            Assert.That(card.Command, Is.Null, "Разворачивать нечего – кнопки быть не должно");
        }
    }

    [Test]
    public void Опрос_без_перемен_не_пересоздаёт_строки_карточки()
    {
        var dispatcher = new ManualUiDispatcher();
        var snapshot = Bus(6);
        var publisher = new DiagnosticsSnapshotPublisher(dispatcher, () => snapshot, NullLogger<DiagnosticsSnapshotPublisher>.Instance);
        var card = new EventBusDiagnosticsCardViewModel(publisher);
        var notified = 0;

        card.SetActive(true);
        card.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(card.Rows) ? 1 : 0;

        snapshot = Bus(6);
        dispatcher.Timers[0].Tick();

        var unchanged = notified;

        snapshot = Bus(6) with { Bus = snapshot.Bus! with { PublishedTotal = 1201 } };
        dispatcher.Timers[0].Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unchanged, Is.Zero, "Новый список строк на каждом тике заставлял страницу заново строить строки всех карточек раз в секунду");
            Assert.That(notified, Is.EqualTo(1), "Изменившееся число обязано дойти до страницы");
            Assert.That(card.Rows[0].Value, Is.EqualTo(DiagnosticsFormat.Number(1201)));
        }
    }

    [Test]
    public void Карточка_памяти_переиспользует_снимок_дочерних_процессов_пока_замер_сторожа_не_обновился()
    {
        var dispatcher = new ManualUiDispatcher();
        var monitor = new PerformanceMonitor(new(), dispatcher, NullLogger<PerformanceMonitor>.Instance);
        var snapshot = DiagnosticsSnapshots.Full();
        var publisher = new DiagnosticsSnapshotPublisher(dispatcher, () => snapshot, NullLogger<DiagnosticsSnapshotPublisher>.Instance);
        var card = new MemoryDiagnosticsCardViewModel(publisher, monitor);

        monitor.Start();
        card.SetActive(true);
        dispatcher.Timers[1].Tick();

        var firstChildren = monitor.Snapshot.Children;
        var firstRows = card.Rows;

        snapshot = snapshot with { CapturedAt = DiagnosticsSnapshots.CapturedAt.AddSeconds(1) };
        dispatcher.Timers[0].Tick();
        dispatcher.Timers[1].Tick();

        var idleChildren = monitor.Snapshot.Children;
        var idleRows = card.Rows;

        snapshot = snapshot with
        {
            CapturedAt = DiagnosticsSnapshots.CapturedAt.AddSeconds(2),
            Memory = snapshot.Memory! with
            {
                MeasuredAt = DiagnosticsSnapshots.CapturedAt.AddSeconds(2),
                ChildBytes = 128L * 1024 * 1024,
            },
        };
        dispatcher.Timers[0].Tick();
        dispatcher.Timers[1].Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstChildren, Is.Not.Null);
            Assert.That(idleChildren, Is.SameAs(firstChildren), "До нового замера сторожа объект ChildProcessUsage не должен аллоцироваться повторно");
            Assert.That(idleRows, Is.SameAs(firstRows));
            Assert.That(monitor.Snapshot.Children, Is.Not.SameAs(firstChildren));
            Assert.That(monitor.Snapshot.Children!.PrivateBytes, Is.EqualTo(128L * 1024 * 1024));
        }

        monitor.Stop();
    }

    [Test]
    public void Карточка_очереди_не_пересобирает_расписание_и_строки_внутри_минутного_окна_и_обновляет_на_границе()
    {
        var dispatcher = new ManualUiDispatcher();
        var baseTime = DiagnosticsSnapshots.CapturedAt;
        var snapshot = DiagnosticsSnapshots.Full() with
        {
            CapturedAt = baseTime,
            ChatQueue = DiagnosticsSnapshots.Full().ChatQueue! with { LastSentAt = baseTime },
            Jobs =
            [
                new(ScheduledJob.StatisticsAutoSave, TimeSpan.FromMinutes(1), baseTime.AddMinutes(5).AddSeconds(30), baseTime.AddMinutes(-10), null),
            ],
        };

        var publisher = new DiagnosticsSnapshotPublisher(dispatcher, () => snapshot, NullLogger<DiagnosticsSnapshotPublisher>.Instance);
        var card = new QueueDiagnosticsCardViewModel(publisher);
        var rowRebuilds = 0;

        card.SetActive(true);
        card.PropertyChanged += (_, e) => rowRebuilds += e.PropertyName == nameof(card.Rows) ? 1 : 0;

        var initialRows = card.Rows;
        var initialHint = initialRows.Single(static r => r.Key == "Следующее автосохранение").Hint!;

        snapshot = snapshot with { CapturedAt = baseTime.AddSeconds(20) };
        dispatcher.Timers[0].Tick();

        var sameWindowRows = card.Rows;

        snapshot = snapshot with
        {
            CapturedAt = baseTime,
            Jobs =
            [
                new(ScheduledJob.StatisticsAutoSave, TimeSpan.FromMinutes(1), baseTime.AddSeconds(45), baseTime, null),
            ],
        };
        dispatcher.Timers[0].Tick();
        var sec45Hint = card.Rows.Single(static r => r.Key == "Следующее автосохранение").Hint!;

        snapshot = snapshot with { CapturedAt = baseTime.AddSeconds(1) };
        dispatcher.Timers[0].Tick();
        var sec44Row = card.Rows.Single(static r => r.Key == "Следующее автосохранение");

        snapshot = snapshot with { CapturedAt = baseTime.AddSeconds(61) };
        dispatcher.Timers[0].Tick();
        var boundaryHint = card.Rows.Single(static r => r.Key == "Следующее автосохранение").Hint!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameWindowRows, Is.SameAs(initialRows));
            Assert.That(rowRebuilds, Is.EqualTo(3));
            Assert.That(sec44Row.Value, Is.EqualTo("через 44 с"));
            Assert.That(sec44Row.Hint, Is.SameAs(sec45Hint), "Строка расписания должна переиспользоваться на посекундном отсчёте");
            Assert.That(sec45Hint, Does.Contain("последний запуск только что"));
            Assert.That(boundaryHint, Does.Contain("1 мин. назад"));
            Assert.That(initialHint, Does.Contain("10 мин. назад"));
        }
    }

    [Test]
    public void Карточка_очереди_обновляет_строки_при_смене_набора_или_порядка_заданий()
    {
        var dispatcher = new ManualUiDispatcher();
        var baseTime = DiagnosticsSnapshots.CapturedAt;
        var jobA = new ScheduledJobStatus(ScheduledJob.Broadcast, TimeSpan.FromMinutes(1), baseTime.AddMinutes(5), baseTime, null);
        var jobB = new ScheduledJobStatus(ScheduledJob.StatisticsAutoSave, TimeSpan.FromMinutes(2), baseTime.AddMinutes(8), baseTime, null);

        var snapshot = DiagnosticsSnapshots.Full() with { CapturedAt = baseTime, Jobs = [jobA, jobB] };
        var publisher = new DiagnosticsSnapshotPublisher(dispatcher, () => snapshot, NullLogger<DiagnosticsSnapshotPublisher>.Instance);
        var card = new QueueDiagnosticsCardViewModel(publisher);

        card.SetActive(true);
        Assert.That(card.Rows.Select(static r => r.Key), Does.Contain("Следующая рассылка"));
        Assert.That(card.Rows.Select(static r => r.Key), Does.Contain("Следующее автосохранение"));

        snapshot = snapshot with { Jobs = [jobA] };
        dispatcher.Timers[0].Tick();
        Assert.That(card.Rows.Select(static r => r.Key), Does.Contain("Следующая рассылка"));
        Assert.That(card.Rows.Select(static r => r.Key), Does.Not.Contain("Следующее автосохранение"));

        snapshot = snapshot with { Jobs = [jobB] };
        dispatcher.Timers[0].Tick();
        Assert.That(card.Rows.Select(static r => r.Key), Does.Contain("Следующее автосохранение"));
        Assert.That(card.Rows.Select(static r => r.Key), Does.Not.Contain("Следующая рассылка"));

        snapshot = snapshot with { Jobs = [jobA, jobB] };
        dispatcher.Timers[0].Tick();
        snapshot = snapshot with { Jobs = [jobB, jobA] };
        dispatcher.Timers[0].Tick();

        var keys = card.Rows.Select(static r => r.Key).ToList();
        Assert.That(keys.IndexOf("Следующее автосохранение"), Is.LessThan(keys.IndexOf("Следующая рассылка")));
    }

    private static DiagnosticsSnapshot Bus(int types)
    {
        var byType = Enumerable.Range(0, types)
            .Select(index => new EventTypeStatistics($"EventType{index}",
                100 - index,
                0,
                TimeSpan.FromMilliseconds(120 - index),
                TimeSpan.FromMilliseconds(80),
                DiagnosticsSnapshots.CapturedAt.AddMinutes(-index)))
            .ToArray();

        return DiagnosticsSnapshots.Empty() with { Bus = new(1200, 0, 8, 0, byType) };
    }

    private static PerformanceMonitor Monitor()
    {
        return new(new(), new ManualUiDispatcher(), NullLogger<PerformanceMonitor>.Instance);
    }

    private static TCard Activate<TCard>(Func<DiagnosticsSnapshotPublisher, TCard> create, DiagnosticsSnapshot snapshot)
        where TCard : DiagnosticsCardViewModel
    {
        var publisher = new DiagnosticsSnapshotPublisher(new ManualUiDispatcher(),
            () => snapshot,
            NullLogger<DiagnosticsSnapshotPublisher>.Instance);

        var card = create(publisher);

        card.SetActive(true);

        return card;
    }
}
