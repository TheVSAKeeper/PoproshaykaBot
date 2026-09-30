using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public class UiAsyncTests
{
    [Test]
    public void Сбой_тика_после_await_уходит_в_лог_а_не_из_таймера()
    {
        var dispatcher = new ManualUiDispatcher();
        var pending = new TaskCompletionSource();
        var failures = new List<Exception>();

        var timer = dispatcher.CreateAsyncTimer(TimeSpan.FromSeconds(1), () => pending.Task, failures.Add);
        timer.Start();

        Assert.DoesNotThrow(dispatcher.Timers[0].Tick, "Тик обязан вернуться сразу, не дожидаясь задачи");
        Assert.That(failures, Is.Empty);

        var failure = new InvalidOperationException("сбой");
        pending.SetException(failure);

        Assert.That(failures, Is.EqualTo(new[] { failure }), "Сбой задачи тика обязан дойти до лога ровно один раз");
    }

    [TestCase(true, false, TestName = "Запрошенная отмена в лог не пишется")]
    [TestCase(false, true, TestName = "Отмена без запроса пишется как сбой")]
    public async Task Отмена_различается_по_токену(bool requested, bool logged)
    {
        using var cts = new CancellationTokenSource();

        if (requested)
        {
            await cts.CancelAsync();
        }

        var failures = new List<Exception>();

        await UiAsync.RunGuardedAsync(
            () => Task.FromException(requested ? new OperationCanceledException(cts.Token) : new OperationCanceledException()),
            failures.Add);

        Assert.That(failures, logged ? Has.Count.EqualTo(1) : Is.Empty);
    }
}
