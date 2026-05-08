using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class UserStatisticsRepositoryTests
{
    [SetUp]
    public void SetUp()
    {
        _repository = new(NullLogger<UserStatisticsRepository>.Instance);
    }

    private UserStatisticsRepository _repository = null!;

    [Test]
    public void TrackMessage_NewUser_RegistersAndIncrements()
    {
        _repository.TrackMessage("u1", "Alice");

        var stats = _repository.GetById("u1");

        Assert.That(stats, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats!.MessageCount, Is.EqualTo(1ul));
            Assert.That(stats.Name, Is.EqualTo("Alice"));
        }
    }

    [Test]
    public void TrackMessage_ExistingUser_IncrementsCounterAndUpdatesName()
    {
        _repository.TrackMessage("u1", "Alice");
        _repository.TrackMessage("u1", "AliceRenamed");

        var stats = _repository.GetById("u1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats!.MessageCount, Is.EqualTo(2ul));
            Assert.That(stats.Name, Is.EqualTo("AliceRenamed"));
        }
    }

    [Test]
    public void TrackMessage_SetsHasChanges()
    {
        Assert.That(_repository.HasChanges, Is.False);

        _repository.TrackMessage("u1", "Alice");

        Assert.That(_repository.HasChanges, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TrackMessage_InvalidUserId_Throws(string? userId)
    {
        Assert.Throws<ArgumentException>(() => _repository.TrackMessage(userId!, "Alice"));
    }

    [Test]
    public void GetByName_TrimsLeadingAtAndIsCaseInsensitive()
    {
        _repository.TrackMessage("u1", "Alice");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.GetByName("@alice"), Is.Not.Null);
            Assert.That(_repository.GetByName("ALICE"), Is.Not.Null);
            Assert.That(_repository.GetByName("bob"), Is.Null);
        }
    }

    [Test]
    public void GetById_ReturnsClone_NotInternalReference()
    {
        _repository.TrackMessage("u1", "Alice");
        var first = _repository.GetById("u1")!;

        first.MessageCount = 999;

        var second = _repository.GetById("u1")!;
        Assert.That(second.MessageCount, Is.EqualTo(1ul), "Mutating a snapshot must not leak into repository state");
    }

    [Test]
    public void GetTop_OrdersByTotalDescending()
    {
        _repository.TrackMessage("u1", "Alice");

        for (var i = 0; i < 3; i++)
        {
            _repository.TrackMessage("u2", "Bob");
        }

        for (var i = 0; i < 2; i++)
        {
            _repository.TrackMessage("u3", "Carol");
        }

        var top = _repository.GetTop(2);

        Assert.That(top, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(top[0].UserId, Is.EqualTo("u2"));
            Assert.That(top[1].UserId, Is.EqualTo("u3"));
        }
    }

    [Test]
    public void IncrementBonusMessages_UnknownUser_ReturnsFalse()
    {
        var result = _repository.IncrementBonusMessages("nope", 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(_repository.HasChanges, Is.False);
        }
    }

    [Test]
    public void IncrementBonusMessages_KnownUser_UpdatesAndMarksChanged()
    {
        _repository.TrackMessage("u1", "Alice");
        _repository.CreateSnapshotAndMarkSaved();

        var result = _repository.IncrementBonusMessages("u1", 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(_repository.GetById("u1")!.BonusMessageCount, Is.EqualTo(5ul));
            Assert.That(_repository.HasChanges, Is.True);
        }
    }

    [Test]
    public void IncrementShtrafMessages_AffectsShtrafCounter()
    {
        _repository.TrackMessage("u1", "Alice");
        _repository.IncrementShtrafMessages("u1", 7);

        Assert.That(_repository.GetById("u1")!.ShtrafMessageCount, Is.EqualTo(7ul));
    }

    [Test]
    public void IncrementBonusMessages_ZeroDelta_DoesNothing()
    {
        _repository.TrackMessage("u1", "Alice");
        _repository.CreateSnapshotAndMarkSaved();

        var result = _repository.IncrementBonusMessages("u1", 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(_repository.HasChanges, Is.False);
        }
    }

    [Test]
    public void CreateSnapshotAndMarkSaved_ResetsHasChanges_AndReturnsClones()
    {
        _repository.TrackMessage("u1", "Alice");
        var snapshot = _repository.CreateSnapshotAndMarkSaved();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.HasChanges, Is.False);
            Assert.That(snapshot, Has.Count.EqualTo(1));
        }

        snapshot[0].MessageCount = 999;
        Assert.That(_repository.GetById("u1")!.MessageCount, Is.EqualTo(1ul));
    }

    [Test]
    public void MarkChanged_AfterSnapshot_RestoresFlag()
    {
        _repository.TrackMessage("u1", "Alice");
        _repository.CreateSnapshotAndMarkSaved();

        _repository.MarkChanged();

        Assert.That(_repository.HasChanges, Is.True);
    }

    [Test]
    public void ReplaceAll_OverwritesContentAndClearsFlag()
    {
        _repository.TrackMessage("u1", "Alice");

        _repository.ReplaceAll([
            UserStatistics.Create("u2", "Bob"),
            UserStatistics.Create("u3", "Carol"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.GetById("u1"), Is.Null);
            Assert.That(_repository.GetById("u2"), Is.Not.Null);
            Assert.That(_repository.HasChanges, Is.False);
        }
    }

    [Test]
    public async Task ConcurrentTrackMessage_DoesNotLoseUpdates()
    {
        const int threadCount = 8;
        const int perThread = 500;

        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    _repository.TrackMessage("u1", "Alice");
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.That(_repository.GetById("u1")!.MessageCount, Is.EqualTo((ulong)(threadCount * perThread)));
    }
}
