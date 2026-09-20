using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels.Diagnostics;

namespace PoproshaykaBot.Wpf.Tests;

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
