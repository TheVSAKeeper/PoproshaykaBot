using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Tests.Polls;

[TestFixture]
public class PollHistoryStoreTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, Substitute.For<IEventBus>());
        _settingsManager.Current.Returns(_settings);
        _helix = Substitute.For<ITwitchHelixClient>();
        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("1");
        _tempFile = Path.Combine(Path.GetTempPath(), $"poll-history-{Guid.NewGuid():N}.json");
        _store = new(_settingsManager, _helix, _broadcasterIdProvider, NullLogger<PollHistoryStore>.Instance, _tempFile);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in new[] { string.Empty, ".bak", ".old", ".tmp" })
        {
            var path = _tempFile + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private PollHistoryStore _store = null!;
    private string _tempFile = null!;

    private static PollHistoryEntry Entry(string id)
    {
        return new()
        {
            PollId = id,
            Title = "T",
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            FinalStatus = PollSnapshotStatus.Completed,
        };
    }

    [Test]
    public void TryAdd_Unique_Persists()
    {
        var added = _store.TryAdd(Entry("p1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.True);
            Assert.That(_store.GetAll(), Has.Count.EqualTo(1));
            Assert.That(File.Exists(_tempFile), Is.True);
        }
    }

    [Test]
    public void TryAdd_Duplicate_Ignored()
    {
        _store.TryAdd(Entry("p1"));
        var added = _store.TryAdd(Entry("p1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.False);
            Assert.That(_store.GetAll(), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void TryAdd_TruncatesToHistoryMaxItems()
    {
        _settings.Twitch.Polls.HistoryMaxItems = 3;

        _store.TryAdd(Entry("p1"));
        _store.TryAdd(Entry("p2"));
        _store.TryAdd(Entry("p3"));
        _store.TryAdd(Entry("p4"));

        var all = _store.GetAll();
        Assert.That(all, Has.Count.EqualTo(3));
        Assert.That(all.Select(e => e.PollId), Is.EquivalentTo(["p2", "p3", "p4"]));
    }

    [Test]
    public void Reload_RestoresEntriesFromDisk()
    {
        _store.TryAdd(Entry("p1"));
        _store.TryAdd(Entry("p2"));

        var second = new PollHistoryStore(_settingsManager, _helix, _broadcasterIdProvider, NullLogger<PollHistoryStore>.Instance, _tempFile);
        var all = second.GetAll();

        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all.Select(e => e.PollId), Is.EquivalentTo(["p1", "p2"]));
    }

    [Test]
    public async Task BackfillAsync_SkipsActivePolls_AddsCompleted()
    {
        var started = DateTime.UtcNow.AddMinutes(-5);
        var ended = DateTime.UtcNow.AddMinutes(-4);

        _helix.GetPollsAsync("1", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new("active", "1", "Active?", [new("c1", "A", 0, 0, 0)], false, 0, "ACTIVE", 60,
                    started, null),
                new("done", "1", "Done?", [new("c1", "A", 5, 0, 0), new("c2", "B", 3, 0, 0)], false, 0, "COMPLETED", 60,
                    started, ended),
            ]);

        var added = await _store.BackfillAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.EqualTo(1));
            Assert.That(_store.GetAll().Select(e => e.PollId), Is.EquivalentTo(["done"]));
        }
    }

    [Test]
    public async Task BackfillAsync_Dedups_AgainstExisting()
    {
        _store.TryAdd(Entry("done"));
        var started = DateTime.UtcNow.AddMinutes(-5);
        var ended = DateTime.UtcNow.AddMinutes(-4);

        _helix.GetPollsAsync("1", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new("done", "1", "Done?", [new("c1", "A", 5, 0, 0)], false, 0, "COMPLETED", 60,
                    started, ended),
            ]);

        var added = await _store.BackfillAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.EqualTo(0));
            Assert.That(_store.GetAll(), Has.Count.EqualTo(1));
        }
    }
}
