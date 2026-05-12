using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class BotStatisticsRepositoryTests
{
    [SetUp]
    public void SetUp()
    {
        _repository = new();
    }

    private BotStatisticsRepository _repository = null!;

    [Test]
    public void GetSnapshot_ReturnsCloneWithFreshUptime()
    {
        var snapshot = _repository.GetSnapshot();

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.TotalUptime, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
    }

    [Test]
    public void GetSnapshot_DoesNotMarkChanges()
    {
        _repository.GetSnapshot();
        Assert.That(_repository.HasChanges, Is.False);
    }

    [Test]
    public void IncrementMessagesProcessed_BumpsCounterAndMarksChanged()
    {
        _repository.IncrementMessagesProcessed();
        _repository.IncrementMessagesProcessed();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.GetSnapshot().TotalMessagesProcessed, Is.EqualTo(2ul));
            Assert.That(_repository.HasChanges, Is.True);
        }
    }

    [Test]
    public async Task ResetStartTime_ResetsAndMarksChanged()
    {
        var before = _repository.GetSnapshot().BotStartTime;
        await Task.Delay(5);

        _repository.ResetStartTime();

        var after = _repository.GetSnapshot().BotStartTime;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(_repository.HasChanges, Is.True);
        }
    }

    [Test]
    public void Replace_OverwritesAndClearsFlag()
    {
        _repository.IncrementMessagesProcessed();
        Assert.That(_repository.HasChanges, Is.True);

        var replacement = BotStatistics.Create();
        replacement.TotalMessagesProcessed = 42;

        _repository.Replace(replacement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.HasChanges, Is.False);
            Assert.That(_repository.GetSnapshot().TotalMessagesProcessed, Is.EqualTo(42ul));
        }
    }

    [Test]
    public void CreateSnapshotAndMarkSaved_ClearsFlag()
    {
        _repository.IncrementMessagesProcessed();
        var snapshot = _repository.CreateSnapshotAndMarkSaved();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.HasChanges, Is.False);
            Assert.That(snapshot.TotalMessagesProcessed, Is.EqualTo(1ul));
        }
    }

    [Test]
    public void MarkChanged_AfterSnapshot_RestoresFlag()
    {
        _repository.IncrementMessagesProcessed();
        _repository.CreateSnapshotAndMarkSaved();

        _repository.MarkChanged();

        Assert.That(_repository.HasChanges, Is.True);
    }

    [Test]
    public async Task ConcurrentIncrement_DoesNotLoseUpdates()
    {
        const int ThreadCount = 8;
        const int PerThread = 500;

        var tasks = Enumerable.Range(0, ThreadCount)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < PerThread; i++)
                {
                    _repository.IncrementMessagesProcessed();
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.That(_repository.GetSnapshot().TotalMessagesProcessed, Is.EqualTo((ulong)(ThreadCount * PerThread)));
    }
}
