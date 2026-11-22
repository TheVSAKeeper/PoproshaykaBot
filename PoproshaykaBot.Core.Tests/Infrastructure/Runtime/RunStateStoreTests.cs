using PoproshaykaBot.Core.Infrastructure.Runtime;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Runtime;

[TestFixture]
public sealed class RunStateStoreTests
{
    private DirectoryInfo _directory = null!;
    private string _markerPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("run-state-tests");
        _markerPath = Path.Combine(_directory.FullName, RunStateStore.FileName);
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Первый_запуск_и_штатный_выход_не_помечают_следующий_запуск_аварийным()
    {
        var store = new RunStateStore(_markerPath);

        var first = store.BeginSession("3.1.0.15", "коммит abc1234");
        Assert.That(first.WasUnclean, Is.False);
        Assert.That(File.Exists(_markerPath), Is.True);

        store.ClearCleanExit();
        Assert.That(File.Exists(_markerPath), Is.False);

        var second = new RunStateStore(_markerPath).BeginSession("3.1.0.15");
        Assert.That(second.WasUnclean, Is.False);
    }

    [Test]
    public void Неудалённый_маркер_опознаётся_как_аварийное_завершение_прошлого_запуска()
    {
        var firstStore = new RunStateStore(_markerPath);
        firstStore.BeginSession("3.1.0.15", "коммит abc1234");

        var secondStore = new RunStateStore(_markerPath);
        var previous = secondStore.BeginSession("3.1.0.15", "коммит def5678");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(previous.WasUnclean, Is.True);
            Assert.That(previous.Record, Is.Not.Null);
            Assert.That(previous.Record!.Status, Is.EqualTo(RunStateStatus.Running));
            Assert.That(previous.Record.Commit, Is.EqualTo("коммит abc1234"));
        }
    }

    [Test]
    public void Фиксация_сбоя_сохраняет_статус_и_тип_исключения_без_текста_сообщения()
    {
        var store = new RunStateStore(_markerPath);
        store.BeginSession("3.1.0.15");

        Exception captured;
        try
        {
            throw new InvalidOperationException("access_token=super-secret-value");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        store.MarkFailure(RunStateStatus.FatalShutdown, captured);

        var rawJson = File.ReadAllText(_markerPath);
        var next = new RunStateStore(_markerPath).BeginSession("3.1.0.15");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rawJson, Does.Not.Contain("super-secret-value"));
            Assert.That(next.WasUnclean, Is.True);
            Assert.That(next.Record!.Status, Is.EqualTo(RunStateStatus.FatalShutdown));
            Assert.That(next.Record.ExceptionType, Does.Contain("System.InvalidOperationException"));
        }
    }

    [Test]
    public void Сторож_памяти_сохраняет_собственный_статус()
    {
        var store = new RunStateStore(_markerPath);
        store.BeginSession("3.1.0.15");
        store.MarkFailure(RunStateStatus.MemoryWatchdog);

        var next = new RunStateStore(_markerPath).BeginSession("3.1.0.15");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(next.WasUnclean, Is.True);
            Assert.That(next.Record!.Status, Is.EqualTo(RunStateStatus.MemoryWatchdog));
        }
    }

    [Test]
    public void Повреждённый_маркер_считается_нештатным_запуском_и_перезаписывается()
    {
        File.WriteAllText(_markerPath, "{ broken json");

        var previous = new RunStateStore(_markerPath).BeginSession("3.1.0.15");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(previous.WasUnclean, Is.True);
            Assert.That(previous.Record, Is.Null);
            Assert.That(File.ReadAllText(_markerPath), Does.Contain("Running"));
        }
    }

    [Test]
    public void Трекер_фоновых_исключений_отделяет_отмены_и_ограничивает_буфер_сбоев()
    {
        var tracker = new UnobservedTaskExceptionTracker(maxEntries: 2);

        var cancelOnly = tracker.Record(new AggregateException(new OperationCanceledException("cancel")));
        var failure1 = tracker.Record(new AggregateException(new InvalidOperationException("secret-1")));
        var failure2 = tracker.Record(new AggregateException(new TimeoutException("secret-2")));
        var failure3 = tracker.Record(new AggregateException(new IOException("secret-3")));

        var snapshot = tracker.Capture();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancelOnly.IsCancellationOnly, Is.True);
            Assert.That(failure1.IsCancellationOnly, Is.False);
            Assert.That(failure2.IsCancellationOnly, Is.False);
            Assert.That(failure3.IsCancellationOnly, Is.False);
            Assert.That(snapshot.CanceledCount, Is.EqualTo(1));
            Assert.That(snapshot.FailureCount, Is.EqualTo(3));
            Assert.That(snapshot.RecentFailures, Has.Count.EqualTo(2));
            Assert.That(snapshot.RecentFailures[0].ExceptionType, Is.EqualTo(typeof(TimeoutException).FullName));
            Assert.That(snapshot.RecentFailures[1].ExceptionType, Is.EqualTo(typeof(IOException).FullName));
        }
    }
}
