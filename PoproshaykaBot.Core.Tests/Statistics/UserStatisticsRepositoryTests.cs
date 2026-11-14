using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class UserStatisticsRepositoryTests
{
    private UserStatisticsRepository _repository = null!;

    private IPlatformUserStatistics Twitch => _repository.ForPlatform(StreamingPlatform.Twitch);

    private static PlatformUserKey Key(string userId, StreamingPlatform platform = StreamingPlatform.Twitch) => new(platform, userId);

    [SetUp]
    public void SetUp()
    {
        _repository = new(NullLogger<UserStatisticsRepository>.Instance);
    }

    [Test]
    public void TrackMessage_NewUser_RegistersAndIncrements()
    {
        _repository.TrackMessage(Key("u1"), "Alice");

        var stats = Twitch.GetById("u1");

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
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.TrackMessage(Key("u1"), "AliceRenamed");

        var stats = Twitch.GetById("u1");

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

        _repository.TrackMessage(Key("u1"), "Alice");

        Assert.That(_repository.HasChanges, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TrackMessage_InvalidUserId_Throws(string? userId)
    {
        Assert.Catch<ArgumentException>(() => _repository.TrackMessage(Key(userId!), "Alice"));
    }

    [Test]
    public void GetByName_TrimsLeadingAtAndIsCaseInsensitive()
    {
        _repository.TrackMessage(Key("u1"), "Alice");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Twitch.GetByName("@alice"), Is.Not.Null);
            Assert.That(Twitch.GetByName("ALICE"), Is.Not.Null);
            Assert.That(Twitch.GetByName("bob"), Is.Null);
        }
    }

    [Test]
    public void GetById_ReturnsClone_NotInternalReference()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        var first = Twitch.GetById("u1")!;

        first.MessageCount = 999;

        var second = Twitch.GetById("u1")!;
        Assert.That(second.MessageCount, Is.EqualTo(1ul), "Mutating a snapshot must not leak into repository state");
    }

    [Test]
    public void GetTop_OrdersByTotalDescending()
    {
        _repository.TrackMessage(Key("u1"), "Alice");

        for (var i = 0; i < 3; i++)
        {
            _repository.TrackMessage(Key("u2"), "Bob");
        }

        for (var i = 0; i < 2; i++)
        {
            _repository.TrackMessage(Key("u3"), "Carol");
        }

        var top = Twitch.GetTop(2);

        Assert.That(top, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(top[0].UserId, Is.EqualTo("u2"));
            Assert.That(top[1].UserId, Is.EqualTo("u3"));
        }
    }

    [Test]
    public void IncrementBonusPoints_UnknownUser_ReturnsFalse()
    {
        var result = _repository.IncrementBonusPoints(Key("nope"), 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(_repository.HasChanges, Is.False);
        }
    }

    [Test]
    public void IncrementBonusPoints_KnownUser_UpdatesAndMarksChanged()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.GetPartition(StreamingPlatform.Twitch).CreateSnapshotAndMarkSaved();

        var result = _repository.IncrementBonusPoints(Key("u1"), 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(Twitch.GetById("u1")!.BonusPoints, Is.EqualTo(5ul));
            Assert.That(_repository.HasChanges, Is.True);
        }
    }

    [Test]
    public void IncrementPenaltyPoints_AffectsPenaltyCounter()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.IncrementPenaltyPoints(Key("u1"), 7);

        Assert.That(Twitch.GetById("u1")!.PenaltyPoints, Is.EqualTo(7ul));
    }

    [Test]
    public void Points_CombineMessagesBonusAndPenalty()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.IncrementBonusPoints(Key("u1"), 10);
        _repository.IncrementPenaltyPoints(Key("u1"), 3);

        var stats = Twitch.GetById("u1")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.MessageCount, Is.EqualTo(2ul));
            Assert.That(stats.BonusPoints, Is.EqualTo(10ul));
            Assert.That(stats.PenaltyPoints, Is.EqualTo(3ul));
            Assert.That(stats.Points, Is.EqualTo(9));
        }
    }

    [Test]
    public void GetTop_ByMessagesMode_IgnoresBonusAndPenalty()
    {
        _repository.TrackMessage(Key("u1"), "Alice");

        for (var i = 0; i < 5; i++)
        {
            _repository.TrackMessage(Key("u2"), "Bob");
        }

        _repository.IncrementBonusPoints(Key("u1"), 100);

        var byMessages = Twitch.GetTop(2, UserTopMode.Messages);
        var byPoints = Twitch.GetTop(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(byMessages[0].UserId, Is.EqualTo("u2"), "Top by messages should rank user with more written messages first");
            Assert.That(byPoints[0].UserId, Is.EqualTo("u1"), "Top by points should rank user with bonus first");
        }
    }

    [Test]
    public void IncrementBonusPoints_ZeroDelta_DoesNothing()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        _repository.GetPartition(StreamingPlatform.Twitch).CreateSnapshotAndMarkSaved();

        var result = _repository.IncrementBonusPoints(Key("u1"), 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(_repository.HasChanges, Is.False);
        }
    }

    [Test]
    public void CreateSnapshotAndMarkSaved_ResetsHasChanges_AndReturnsClones()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        var snapshot = _repository.GetPartition(StreamingPlatform.Twitch).CreateSnapshotAndMarkSaved();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_repository.HasChanges, Is.False);
            Assert.That(snapshot, Has.Count.EqualTo(1));
        }

        snapshot[0].MessageCount = 999;
        Assert.That(Twitch.GetById("u1")!.MessageCount, Is.EqualTo(1ul));
    }

    [Test]
    public void MarkChanged_AfterSnapshot_RestoresFlag()
    {
        _repository.TrackMessage(Key("u1"), "Alice");
        var partition = _repository.GetPartition(StreamingPlatform.Twitch);
        partition.CreateSnapshotAndMarkSaved();

        partition.MarkChanged();

        Assert.That(_repository.HasChanges, Is.True);
    }

    [Test]
    public void SeedPlatform_OverwritesContentAndClearsFlag()
    {
        _repository.TrackMessage(Key("u1"), "Alice");

        _repository.SeedPlatform(StreamingPlatform.Twitch, [
            UserStatistics.Create(StreamingPlatform.Twitch, "u2", "Bob"),
            UserStatistics.Create(StreamingPlatform.Twitch, "u3", "Carol"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Twitch.GetById("u1"), Is.Null);
            Assert.That(Twitch.GetById("u2"), Is.Not.Null);
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
                    _repository.TrackMessage(Key("u1"), "Alice");
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.That(Twitch.GetById("u1")!.MessageCount, Is.EqualTo((ulong)(threadCount * perThread)));
    }

    [Test]
    public void SameUserIdAcrossTwitchAndYouTube_RemainsIsolated()
    {
        var twitchKey = Key("shared-id", StreamingPlatform.Twitch);
        var youtubeKey = Key("shared-id", StreamingPlatform.YouTube);

        _repository.TrackMessage(twitchKey, "Alice");
        _repository.TrackMessage(twitchKey, "Alice");
        _repository.TrackMessage(youtubeKey, "Alice");
        _repository.IncrementBonusPoints(twitchKey, 50);
        _repository.IncrementPenaltyPoints(youtubeKey, 1);

        var twitchPartition = _repository.GetPartition(StreamingPlatform.Twitch);
        var youtubePartition = _repository.GetPartition(StreamingPlatform.YouTube);
        twitchPartition.CreateSnapshotAndMarkSaved();

        var twitchStats = _repository.ForPlatform(StreamingPlatform.Twitch).GetById("shared-id")!;
        var youtubeStats = _repository.ForPlatform(StreamingPlatform.YouTube).GetById("shared-id")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(twitchStats.Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(twitchStats.MessageCount, Is.EqualTo(2ul));
            Assert.That(twitchStats.Points, Is.EqualTo(52));
            Assert.That(youtubeStats.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(youtubeStats.MessageCount, Is.EqualTo(1ul));
            Assert.That(youtubeStats.Points, Is.EqualTo(0));
            Assert.That(_repository.ForPlatform(StreamingPlatform.Twitch).GetByName("Alice")!.Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(_repository.ForPlatform(StreamingPlatform.YouTube).GetByName("Alice")!.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(_repository.ForPlatform(StreamingPlatform.Twitch).GetTop(10).Select(static u => u.Points), Is.EqualTo(new[] { 52L }));
            Assert.That(_repository.ForPlatform(StreamingPlatform.YouTube).GetTop(10).Select(static u => u.Points), Is.EqualTo(new[] { 0L }));
            Assert.That(twitchPartition.HasChanges, Is.False);
            Assert.That(youtubePartition.HasChanges, Is.True);
            Assert.That(_repository.GetAll(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void PlatformValueTypes_RejectInvalidPlatformAndBlankIds()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlatformUserKey((StreamingPlatform)99, "u1"));
            Assert.Throws<ArgumentException>(() => _ = new PlatformUserKey(StreamingPlatform.YouTube, "   "));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlatformChannel((StreamingPlatform)99, "ch"));
            Assert.Throws<ArgumentException>(() => _ = new PlatformChannel(StreamingPlatform.YouTube, string.Empty));
        }
    }
}
