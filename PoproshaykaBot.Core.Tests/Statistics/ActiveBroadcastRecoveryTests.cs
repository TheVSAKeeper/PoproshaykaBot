using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using PoproshaykaBot.Core.Tests.Server;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

public sealed partial class ActiveBroadcastStoreTests
{
    private static ActiveBroadcast CompletedDraft()
    {
        var start = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        return new()
        {
            BroadcastId = Guid.NewGuid(),
            StartedAt = start,
            UpdatedAt = start.AddHours(1),
            EndedAt = start.AddHours(1),
            Platforms = [new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "twitch-chan",
                StartedAt = start,
                UpdatedAt = start.AddHours(1),
                EndedAt = start.AddHours(1),
                Segments = [new() { StartedAt = start, EndedAt = start.AddHours(1), MessageCount = 9 }],
            }],
        };
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DelayedCheckpoint_CannotReplaceFinalDraftOrResurrectPersistedBroadcast(bool deleteFinal)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new BlockingCheckpointStore(_historyStore, _activePath, _legacyPath, entered, release);
        var completed = CompletedDraft();
        var checkpoint = JsonStoreClone.DeepClone(completed);
        checkpoint.EndedAt = null;
        var delayed = Task.Run(() => store.TrySave(checkpoint));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        try
        {
            Assert.That(store.TrySave(completed), Is.True);
            if (deleteFinal)
            {
                Assert.That(_historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(completed)), Is.True);
                Assert.That(store.TryDelete(), Is.True);
            }
        }
        finally
        {
            release.Set();
        }

        Assert.That(await delayed.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        var reloaded = CreateStore();
        if (deleteFinal)
        {
            Assert.That(reloaded.Load(), Is.Null);
            Assert.That(File.Exists(_activePath), Is.False);
        }
        else
        {
            Assert.That(reloaded.Load()!.EndedAt, Is.EqualTo(completed.EndedAt));
        }
    }

    [Test]
    public void FinalSnapshot_WithEarlierEndThanCheckpoint_IsPersistedBeforeHistoryRetry()
    {
        var completed = CompletedDraft();
        var checkpoint = JsonStoreClone.DeepClone(completed);
        checkpoint.EndedAt = null;
        checkpoint.UpdatedAt = completed.UpdatedAt.AddMinutes(5);
        var store = CreateStore();
        Assert.That(store.TrySave(checkpoint), Is.True);
        Directory.CreateDirectory(Path.Combine(_directory, "broadcasts.json.tmp"));
        var coordinator = new BroadcastDraftCoordinator(_historyStore, store, new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(), NullLogger<BroadcastDraftCoordinator>.Instance);

        coordinator.FinalizeCompletedDraft(completed);

        var reloadedHistory = new BroadcastHistoryStore(null, Path.Combine(_directory, "broadcasts.json"), Path.Combine(_directory, "stream_sessions.json"));
        var reloaded = new ActiveBroadcastStore(reloadedHistory, null, _activePath, _legacyPath).Load();
        Assert.That(reloaded!.EndedAt, Is.EqualTo(completed.EndedAt));
        Assert.That(reloaded.Platforms.Single().Segments.Single().MessageCount, Is.EqualTo(9));
        Directory.Delete(Path.Combine(_directory, "broadcasts.json.tmp"));
        var restartedStore = new ActiveBroadcastStore(reloadedHistory, null, _activePath, _legacyPath);
        var restartedCoordinator = new BroadcastDraftCoordinator(reloadedHistory, restartedStore, new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(), NullLogger<BroadcastDraftCoordinator>.Instance);
        restartedCoordinator.RecoverCompletedDraftOnStartup();
        restartedCoordinator.RecoverCompletedDraftOnStartup();
        Assert.That(new BroadcastHistoryStore(null, Path.Combine(_directory, "broadcasts.json"), Path.Combine(_directory, "stream_sessions.json"))
            .Load().Broadcasts.Single().MessageCount, Is.EqualTo(9));
        Assert.That(File.Exists(_activePath), Is.False);
    }

    [Test]
    public void MergedLegacyDraft_AfterRestartIsNotImportedAgain()
    {
        var first = CompletedDraft();
        var fragment = CompletedDraft();
        fragment.StartedAt = fragment.StartedAt.AddMinutes(30);
        fragment.Platforms[0].StartedAt = fragment.StartedAt;
        fragment.Platforms[0].Segments[0].StartedAt = fragment.StartedAt;
        _historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(first));
        _historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(fragment));
        var sourcePath = Path.Combine(_directory, "broadcasts.json");
        var projectionPath = Path.Combine(_directory, "stream_sessions.json");
        var merged = new BroadcastHistoryStore(null, sourcePath, projectionPath);
        File.WriteAllText(_legacyPath, JsonSerializer.Serialize(new ActiveStreamSession
        {
            Channel = "twitch-chan",
            StartedAt = fragment.StartedAt,
            UpdatedAt = fragment.UpdatedAt,
            Segments = fragment.Platforms[0].Segments,
        }, JsonStoreOptions.Default));

        var restarted = new ActiveBroadcastStore(merged, null, _activePath, _legacyPath);
        Assert.That(restarted.Load(), Is.Null);
        Assert.That(File.Exists(_legacyPath), Is.False);
        Assert.That(File.Exists(_activePath), Is.False);
        Assert.That(new BroadcastHistoryStore(null, sourcePath, projectionPath).Load().Broadcasts.Single().MessageCount, Is.EqualTo(18));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullDraft_LegacyFileMissingOrChangedCannotReplaceItsData(bool legacyChanged)
    {
        var draft = CompletedDraft();
        draft.EndedAt = null;
        draft.Platforms[0].EndedAt = null;
        draft.Platforms[0].Segments[0].EndedAt = null;
        Assert.That(CreateStore().TrySave(draft), Is.True);
        var originalBytes = File.ReadAllText(_activePath);
        if (legacyChanged)
        {
            File.WriteAllText(_legacyPath, JsonSerializer.Serialize(new ActiveStreamSession
            {
                Channel = "another-channel",
                StartedAt = draft.StartedAt.AddHours(3),
                UpdatedAt = draft.UpdatedAt.AddHours(3),
                Segments = [new() { StartedAt = draft.StartedAt.AddHours(3), MessageCount = 999 }],
            }, JsonStoreOptions.Default));
        }

        var history = new BroadcastHistoryStore(null, Path.Combine(_directory, "broadcasts.json"), Path.Combine(_directory, "stream_sessions.json"));
        var reloaded = new ActiveBroadcastStore(history, null, _activePath, _legacyPath);
        Assert.That(reloaded.Load()!.BroadcastId, Is.EqualTo(draft.BroadcastId));
        Assert.That(reloaded.Load()!.Platforms.Single().Segments.Single().MessageCount, Is.EqualTo(9));
        Assert.That(reloaded.CanWrite, Is.True);
        Assert.That(File.ReadAllText(_activePath), Is.EqualTo(originalBytes));
        Assert.That(File.Exists(_legacyPath), Is.EqualTo(legacyChanged));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedNoOpImport_PreservesDraftWriteRights(bool touched)
    {
        var draft = CompletedDraft();
        draft.EndedAt = null;
        var store = CreateStore();
        Assert.That(store.TrySave(draft), Is.True);
        Assert.That(store.Invalidate(), Is.True);
        store.Restore(!touched);
        var before = File.ReadAllText(_activePath);

        store.Restore(store.Invalidate());
        draft.UpdatedAt = draft.UpdatedAt.AddMinutes(1);
        Assert.That(store.TrySave(draft), Is.EqualTo(!touched));
        Assert.That(store.CanWrite, Is.EqualTo(!touched));
        if (touched)
        {
            Assert.That(File.ReadAllText(_activePath), Is.EqualTo(before));
        }
    }

    [Test]
    public void CompletedDraft_AfterHistoryWriteButBeforeDelete_IsRecoveredOnlyOnce()
    {
        var completed = CompletedDraft();
        var store = CreateStore();
        Assert.That(store.TrySave(completed), Is.True);
        Assert.That(_historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(completed)), Is.True);

        var sourcePath = Path.Combine(_directory, "broadcasts.json");
        var projectionPath = Path.Combine(_directory, "stream_sessions.json");
        var history = new BroadcastHistoryStore(null, sourcePath, projectionPath);
        var restarted = new ActiveBroadcastStore(history, null, _activePath, _legacyPath);
        var coordinator = new BroadcastDraftCoordinator(history, restarted, new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(), NullLogger<BroadcastDraftCoordinator>.Instance);
        coordinator.RecoverCompletedDraftOnStartup();
        coordinator.RecoverCompletedDraftOnStartup();

        var onDisk = JsonSerializer.Deserialize<BroadcastHistoryDocument>(File.ReadAllText(sourcePath), JsonStoreOptions.Default)!;
        Assert.That(onDisk.Broadcasts.Single().MessageCount, Is.EqualTo(9));
        Assert.That(File.Exists(_activePath), Is.False);
    }

    [Test]
    public void BlockedNewDraft_ReportsFailureUntilPendingBroadcastIsPersisted()
    {
        var store = CreateStore();
        var pending = CompletedDraft();
        store.TrySave(pending);
        var next = CompletedDraft();
        next.EndedAt = null;
        Assert.That(store.TrySave(next), Is.False);
        Assert.That(store.LastSaveError, Does.Contain("предыдущий эфир"));
        _historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(pending));
        Assert.That(store.TrySave(next), Is.True);
        Assert.That(store.LastSaveError, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedFinalSnapshot_IsReportedAndHistoryRemainsTheFallback(bool failHistory)
    {
        var store = CreateStore();
        Directory.CreateDirectory(_activePath + ".tmp");
        if (failHistory)
        {
            Directory.CreateDirectory(Path.Combine(_directory, "broadcasts.json.tmp"));
        }

        var logger = new RecordingLogger<BroadcastDraftCoordinator>();
        var coordinator = new BroadcastDraftCoordinator(_historyStore, store, new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(), logger);
        var completed = CompletedDraft();
        coordinator.FinalizeCompletedDraft(completed);
        Assert.That(logger.Entries.Any(entry => entry.Message.Contains("не сохранён", StringComparison.Ordinal)), Is.True);
        Assert.That(_historyStore.IsBroadcastPersistedOnDisk(completed.BroadcastId), Is.EqualTo(!failHistory));
        if (failHistory)
        {
            Assert.That(logger.Entries.Any(entry => entry.Message.Contains("только в памяти", StringComparison.Ordinal)), Is.True);
            Assert.That(store.LastSaveError, Is.Not.Empty);
            Directory.Delete(Path.Combine(_directory, "broadcasts.json.tmp"));
            Assert.That(_historyStore.TryFlush(), Is.True);
            Assert.That(_historyStore.IsBroadcastPersistedOnDisk(completed.BroadcastId), Is.True);
        }
    }

    [Test]
    public void CompletedMergedFragmentDraft_AfterRestartIsDeletedWithoutDoubleCounting()
    {
        var first = CompletedDraft();
        var fragment = CompletedDraft();
        fragment.StartedAt = fragment.StartedAt.AddMinutes(30);
        fragment.UpdatedAt = fragment.UpdatedAt.AddMinutes(30);
        fragment.EndedAt = fragment.EndedAt!.Value.AddMinutes(30);
        fragment.Platforms[0].StartedAt = fragment.StartedAt;
        fragment.Platforms[0].UpdatedAt = fragment.UpdatedAt;
        fragment.Platforms[0].EndedAt = fragment.EndedAt;
        fragment.Platforms[0].Segments[0].StartedAt = fragment.StartedAt;
        fragment.Platforms[0].Segments[0].EndedAt = fragment.EndedAt;
        _historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(first));
        _historyStore.TryAppend(ActiveBroadcastMapper.ToBroadcastRecord(fragment));
        var sourcePath = Path.Combine(_directory, "broadcasts.json");
        var projectionPath = Path.Combine(_directory, "stream_sessions.json");
        var merged = new BroadcastHistoryStore(null, sourcePath, projectionPath);
        Assert.That(merged.Load().Broadcasts.Single().MessageCount, Is.EqualTo(18));
        File.WriteAllText(_activePath, JsonSerializer.Serialize(fragment, JsonStoreOptions.Default));

        var restarted = new ActiveBroadcastStore(merged, null, _activePath, _legacyPath);
        var coordinator = new BroadcastDraftCoordinator(merged, restarted, new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(), NullLogger<BroadcastDraftCoordinator>.Instance);
        coordinator.RecoverCompletedDraftOnStartup();

        var reloaded = new BroadcastHistoryStore(null, sourcePath, projectionPath);
        Assert.That(reloaded.Load().Broadcasts.Single().MessageCount, Is.EqualTo(18));
        Assert.That(reloaded.IsBroadcastPersistedOnDisk(fragment.BroadcastId), Is.True);
        Assert.That(File.Exists(_activePath), Is.False);
    }

    private sealed class BlockingCheckpointStore(
        BroadcastHistoryStore history,
        string activePath,
        string legacyPath,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : ActiveBroadcastStore(history, null, activePath, legacyPath)
    {
        public override bool TrySave(ActiveBroadcast draft)
        {
            if (draft.EndedAt is null)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException();
                }
            }

            return base.TrySave(draft);
        }
    }
}
