using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

public sealed partial class BroadcastHistoryStoreTests
{
    private BroadcastHistoryStore Reload() => new(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);

    [TestCase(true)]
    [TestCase(false)]
    public void Reload_HiddenRecordCannotAbsorbVisibleNeighbor(bool hiddenFirst)
    {
        var first = CreateTwitchRecord("chan", 10);
        var second = CreateTwitchRecord("chan", 20, first.StartedAt.AddHours(1));
        var hidden = hiddenFirst ? first : second;
        hidden.IsHidden = true;
        _store.TryAppend(first);
        _store.TryAppend(second);

        var records = Reload().Load().Broadcasts;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records.Single(record => record.Id == hidden.Id).IsHidden, Is.True);
            Assert.That(records.Single(record => record.Id != hidden.Id).IsHidden, Is.False);
            Assert.That(records.Sum(record => record.MessageCount), Is.EqualTo(30));
        }
    }

    [Test]
    public void Reload_ProjectionWriteFailureDoesNotUndoSourceHiddenState()
    {
        var record = CreateTwitchRecord("chan", 10);
        _store.TryAppend(record);
        Directory.CreateDirectory(_sessionsPath + ".tmp");
        Assert.That(_store.TrySetHidden(record.Id, true), Is.True);
        var pending = JsonSerializer.Deserialize<BroadcastHistoryDocument>(File.ReadAllText(_broadcastsPath), JsonStoreOptions.Default)!;
        Assert.That(pending.ProjectionOutOfDate, Is.True);
        Directory.Delete(_sessionsPath + ".tmp");

        var reloaded = Reload();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Load().Broadcasts.Single().IsHidden, Is.True);
            var projection = JsonSerializer.Deserialize<StreamSessionHistory>(File.ReadAllText(_sessionsPath), JsonStoreOptions.Default)!;
            Assert.That(projection.Sessions.Single().IsHidden, Is.True);
            Assert.That(Reload().Load().Broadcasts.Single().IsHidden, Is.True);
        }
    }

    [Test]
    public void Reload_UnreadableStaleProjectionRetainsPendingMarkerAcrossSourceWrites()
    {
        var record = CreateTwitchRecord("chan", 10);
        _store.TryAppend(record);
        Assert.That(_store.TrySetHidden(record.Id, true), Is.True);
        Directory.CreateDirectory(_sessionsPath + ".tmp");
        Assert.That(_store.TrySetHidden(record.Id, false), Is.True);
        Directory.Delete(_sessionsPath + ".tmp");

        using (new FileStream(_sessionsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blocked = Reload();
            Assert.That(blocked.TryAppend(CreateYouTubeOnlyRecord("yt", 3)), Is.True);
            var source = JsonSerializer.Deserialize<BroadcastHistoryDocument>(File.ReadAllText(_broadcastsPath), JsonStoreOptions.Default)!;
            Assert.That(source.ProjectionOutOfDate, Is.True);
        }

        var reloaded = Reload();
        Assert.That(reloaded.Load().Broadcasts.Single(item => item.Id == record.Id).IsHidden, Is.False);
        Assert.That(reloaded.LoadSessionsProjection().Sessions.Single().IsHidden, Is.False);
    }

    [Test]
    public void Reload_MergedLegacyFragmentIsRecognizedByItsSegmentStart()
    {
        var first = CreateTwitchRecord("chan", 10);
        var fragment = CreateTwitchRecord("chan", 20, first.StartedAt.AddMinutes(30));
        _store.TryAppend(first);
        _store.TryAppend(fragment);

        var reloaded = Reload();
        Assert.That(reloaded.Load().Broadcasts, Has.Count.EqualTo(1));
        Assert.That(reloaded.HasPersistedOrImportedTwitchSession("chan", fragment.StartedAt), Is.True);
    }

    [Test]
    public void Reload_MergedFragmentIdRemainsIdempotentAcrossRestarts()
    {
        var first = CreateTwitchRecord("chan", 10);
        var fragment = CreateTwitchRecord("chan", 20, first.StartedAt.AddHours(1));
        _store.TryAppend(first);
        _store.TryAppend(fragment);

        var merged = Reload();
        Assert.That(merged.IsBroadcastPersistedOnDisk(fragment.Id), Is.True);
        Assert.That(merged.TryAppend(fragment), Is.True);
        var reloaded = Reload();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Load().Broadcasts, Has.Count.EqualTo(1));
            Assert.That(reloaded.Load().Broadcasts[0].MessageCount, Is.EqualTo(30));
            Assert.That(reloaded.IsBroadcastPersistedOnDisk(fragment.Id), Is.True);
        }
    }

    [Test]
    public void Reload_StaleProjectionKeepsSourceStateButImportsNewOldBuildSessions()
    {
        var original = CreateTwitchRecord("chan", 10);
        _store.TryAppend(original);
        Directory.CreateDirectory(_sessionsPath + ".tmp");
        Assert.That(_store.TrySetHidden(original.Id, true), Is.True);
        Directory.Delete(_sessionsPath + ".tmp");
        var next = CreateTwitchRecord("chan", 20, original.EndedAt.AddHours(1));
        original.TryProjectToTwitchSession(out var oldProjection);
        next.TryProjectToTwitchSession(out var nextProjection);
        File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(new StreamSessionHistory
        {
            Sessions = [oldProjection, nextProjection],
        }, JsonStoreOptions.Default));

        var records = Reload().Load().Broadcasts;
        Assert.That(records, Has.Count.EqualTo(2));
        Assert.That(records.Single(record => record.Id == original.Id).IsHidden, Is.True);
        Assert.That(records.Single(record => record.Id == next.Id).MessageCount, Is.EqualTo(20));
        Assert.That(Reload().Load().Broadcasts, Has.Count.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Reload_CorruptProjectionUsesSourceAndRetainsInvalidCopy(bool denyBackup)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Проверка отказа создания копии использует ACL Windows.");
            return;
        }

        var record = CreateTwitchRecord("chan", 10);
        _store.TryAppend(record);
        File.WriteAllText(_sessionsPath, "{broken");
        var directory = new DirectoryInfo(_directory);
        var permissions = directory.GetAccessControl();
        try
        {
            if (denyBackup)
            {
                var restricted = directory.GetAccessControl();
                restricted.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                directory.SetAccessControl(restricted);
            }

            var reloaded = Reload();
            Assert.That(reloaded.Load().Broadcasts.Single().Id, Is.EqualTo(record.Id));
            Assert.That(reloaded.CanWriteProjection, Is.EqualTo(!denyBackup));
            if (denyBackup)
            {
                Assert.That(File.ReadAllText(_sessionsPath), Is.EqualTo("{broken"));
                Assert.That(Directory.GetFiles(_directory, "stream_sessions.invalid-*"), Is.Empty);
            }
            else
            {
                Assert.That(Directory.GetFiles(_directory, "stream_sessions.invalid-*").Select(File.ReadAllText), Is.EquivalentTo(new[] { "{broken" }));
            }
        }
        finally
        {
            if (denyBackup)
            {
                directory.SetAccessControl(permissions);
            }
        }

        Assert.That(Reload().LoadSessionsProjection().Sessions.Single().Id, Is.EqualTo(record.Id));
    }

    [Test]
    public void Reload_UnreadableProjectionCannotBeOverwrittenWithoutInvalidCopy()
    {
        _store.TryAppend(CreateTwitchRecord("chan", 10));
        var before = File.ReadAllText(_sessionsPath);
        using (new FileStream(_sessionsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var reloaded = Reload();
            Assert.That(reloaded.CanWriteSource, Is.True);
            Assert.That(reloaded.CanWriteProjection, Is.False);
            Assert.That(reloaded.TryAppend(CreateYouTubeOnlyRecord("yt", 3)), Is.True);
        }

        Assert.That(File.ReadAllText(_sessionsPath), Is.EqualTo(before));
        Assert.That(Reload().Load().Broadcasts, Has.Count.EqualTo(2));
    }

    [Test]
    public void Reload_CorruptSourceDoesNotReplaceItFromValidProjection()
    {
        _store.TryAppend(CreateTwitchRecord("chan", 10));
        var projection = File.ReadAllText(_sessionsPath);
        File.WriteAllText(_broadcastsPath, "{broken");
        var reloaded = Reload();
        Assert.That(reloaded.TryAppend(CreateTwitchRecord("another", 3)), Is.False);
        Assert.That(reloaded.TryFlush(), Is.False);
        Assert.That(File.ReadAllText(_broadcastsPath), Is.EqualTo("{broken"));
        Assert.That(File.ReadAllText(_sessionsPath), Is.EqualTo(projection));
    }

    [Test]
    public void FailedSourceWrite_CanRetryWithoutDuplicatingRecord()
    {
        var record = CreateTwitchRecord("chan", 10);
        Directory.CreateDirectory(_broadcastsPath + ".tmp");
        Assert.That(_store.TryAppend(record), Is.False);
        Assert.That(File.Exists(_sessionsPath), Is.False);
        Directory.Delete(_broadcastsPath + ".tmp");
        Assert.That(_store.TryAppend(record), Is.True);
        Assert.That(Reload().Load().Broadcasts.Single().MessageCount, Is.EqualTo(10));
    }

    [Test]
    public void DifferentIdsAtSameStart_ArePreservedAndRecognizedAfterMerge()
    {
        var first = CreateTwitchRecord("chan", 10);
        var second = CreateTwitchRecord("chan", 20, first.StartedAt);
        _store.TryAppend(first);
        _store.TryAppend(second);
        var reloaded = Reload();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.IsBroadcastPersistedOnDisk(first.Id), Is.True);
            Assert.That(reloaded.IsBroadcastPersistedOnDisk(second.Id), Is.True);
            Assert.That(reloaded.Load().Broadcasts.Sum(record => record.MessageCount), Is.EqualTo(30));
        }
    }

    [Test]
    public void Reload_OldBuildImportsThenMergesAndTransfersNewHiddenState()
    {
        var first = CreateTwitchRecord("chan", 10);
        _store.TryAppend(first);
        var second = CreateTwitchRecord("chan", 20, first.StartedAt.AddHours(1));
        first.IsHidden = true;
        first.TryProjectToTwitchSession(out var firstProjection);
        second.TryProjectToTwitchSession(out var secondProjection);
        File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(new StreamSessionHistory
        {
            Sessions = [firstProjection, secondProjection],
        }, JsonStoreOptions.Default));

        var history = Reload();
        var record = history.Load().Broadcasts.Single();
        Assert.That(record.IsHidden, Is.True);
        Assert.That(record.MessageCount, Is.EqualTo(30));
        Assert.That(history.IsBroadcastPersistedOnDisk(second.Id), Is.True);
        Assert.That(Reload().Load().Broadcasts.Single().MessageCount, Is.EqualTo(30));
    }

    [Test]
    public void Reload_ProjectionWithPreviouslyMergedIdsDoesNotImportThemAgain()
    {
        var first = CreateTwitchRecord("chan", 10);
        var second = CreateTwitchRecord("chan", 20, first.StartedAt.AddHours(1));
        _store.TryAppend(first);
        _store.TryAppend(second);
        Assert.That(Reload().Load().Broadcasts.Single().MessageCount, Is.EqualTo(30));
        first.TryProjectToTwitchSession(out var firstProjection);
        second.TryProjectToTwitchSession(out var secondProjection);
        File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(new StreamSessionHistory
        {
            Sessions = [firstProjection, secondProjection],
        }, JsonStoreOptions.Default));
        Assert.That(Reload().Load().Broadcasts.Single().MessageCount, Is.EqualTo(30));
    }
}
