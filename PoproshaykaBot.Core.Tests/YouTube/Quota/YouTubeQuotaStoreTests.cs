using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Tests.Statistics;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.Tests.YouTube.Quota;

[TestFixture]
public class YouTubeQuotaStoreTests
{
    private string _directory = null!;
    private string _quotaPath = null!;
    private string _settingsPath = null!;
    private TestTimeProvider _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-quota-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_directory, "youtube"));
        _quotaPath = Path.Combine(_directory, LegacyDataCatalog.YouTubeQuotaFileName);
        _settingsPath = Path.Combine(_directory, "youtube.json");
        _clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero) };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Valid_File_Persists_And_Reloads_Counters()
    {
        var settingsStore = CreateSettingsStore();

        using (var store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath))
        {
            var gov = new YouTubeQuotaGovernor(store, settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
            Assert.That(gov.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 42, out var lease), Is.True);
            lease.Commit();
        }

        using var reloadedStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath);
        var reloadedGov = new YouTubeQuotaGovernor(reloadedStore, settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
        var status = reloadedGov.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(status.CommittedUnits, Is.EqualTo(42));
            Assert.That(status.BotCommittedUnits, Is.EqualTo(42));
            Assert.That(status.Reliability, Is.EqualTo(YouTubeQuotaReliability.Reliable));
            Assert.That(status.CanWriteFile, Is.True);
        });
    }

    [TestCase("{broken json")]
    [TestCase("   ")]
    [TestCase("null")]
    [TestCase("""{"schemaVersion":99,"projects":{}}""")]
    [TestCase("""{"schemaVersion":1,"projects":{"p":{"pacificDate":"2026-11-15","dailyBudget":10000,"committedUnits":-5,"botCommittedUnits":0,"broadcasterCommittedUnits":0,"exhaustedByServer":false}}}""")]
    public void Corrupt_File_Creates_Invalid_Backup_And_Never_Overwrites_Original(string corruptContent)
    {
        File.WriteAllText(_quotaPath, corruptContent);
        var settingsStore = CreateSettingsStore();

        using var store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath);
        var governor = new YouTubeQuotaGovernor(store, settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
        var status = governor.CaptureDiagnostics();

        var backupFiles = Directory.GetFiles(Path.GetDirectoryName(_quotaPath)!, "*.invalid*");

        Assert.Multiple(() =>
        {
            Assert.That(status.Reliability, Is.EqualTo(YouTubeQuotaReliability.CorruptFile));
            Assert.That(status.CanWriteFile, Is.False);
            Assert.That(backupFiles, Is.Not.Empty);
            Assert.That(governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.StandaloneGreeting, 1, out _), Is.False);
            Assert.That(governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.ScheduledBroadcast, 1, out _), Is.False);
            Assert.That(governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var tier1Lease), Is.True);
            tier1Lease.Commit();
            Assert.That(File.ReadAllText(_quotaPath), Is.EqualTo(corruptContent));
        });
    }

    [Test]
    public async Task External_Write_For_YouTube_Quota_Freezes_Only_QuotaStore()
    {
        var settingsStore = CreateSettingsStore();
        using var store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath);
        var gov = new YouTubeQuotaGovernor(store, settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
        var graph = new StatisticsTestGraph(_directory, extraParticipants: store);

        Assert.That(gov.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var beforeImport), Is.True);
        beforeImport.Commit();

        await graph.AutoSaver.RunExternalWriteAsync(
            () =>
            {
                File.WriteAllText(_quotaPath, """{"schemaVersion":1,"projects":{"proj":{"pacificDate":"2026-11-15","dailyBudget":10000,"committedUnits":900,"botCommittedUnits":900,"broadcasterCommittedUnits":0,"exhaustedByServer":false}}}""");
                return true;
            },
            _ => StatisticsExternalWrite.Files(LegacyDataCatalog.YouTubeQuotaFileName));

        Assert.That(gov.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var afterImport), Is.True);
        afterImport.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(store.CanWriteFile, Is.False);
            Assert.That(File.ReadAllText(_quotaPath), Does.Contain("\"committedUnits\":900"));
        });
    }

    [Test]
    public async Task External_Write_For_Other_Files_Restores_QuotaStore_Writability()
    {
        var settingsStore = CreateSettingsStore();
        using var store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath);
        var gov = new YouTubeQuotaGovernor(store, settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
        var graph = new StatisticsTestGraph(_directory, extraParticipants: store);

        await graph.AutoSaver.RunExternalWriteAsync(
            () => true,
            _ => StatisticsExternalWrite.Files(LegacyDataCatalog.UserStatisticsFileName));

        Assert.That(gov.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 7, out var lease), Is.True);
        lease.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(store.CanWriteFile, Is.True);
            Assert.That(File.ReadAllText(_quotaPath), Does.Contain("\"committedUnits\": 7"));
        });
    }

    [Test]
    public async Task InvalidateAsync_Before_First_Read_And_Stale_SaveSnapshot_Sequence_Work_Safely()
    {
        await File.WriteAllTextAsync(_quotaPath, """{"schemaVersion":1,"projects":{"proj":{"pacificDate":"2026-11-15","dailyBudget":10000,"committedUnits":80,"botCommittedUnits":80,"broadcasterCommittedUnits":0,"exhaustedByServer":false}}}""");

        using var store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, _quotaPath);
        await ((IExternalWriteParticipant)store).InvalidateAsync(CancellationToken.None);

        Assert.That(store.EnsureLoaded().Projects["proj"].CommittedUnits, Is.EqualTo(80));

        store.SaveSnapshot(
            new YouTubeQuotaDocument
            {
                Projects = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["proj"] = new YouTubeQuotaProjectRecord { PacificDate = "2026-11-15", CommittedUnits = 200 },
                },
            },
            sequence: 2);

        store.SaveSnapshot(
            new YouTubeQuotaDocument
            {
                Projects = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["proj"] = new YouTubeQuotaProjectRecord { PacificDate = "2026-11-15", CommittedUnits = 10 },
                },
            },
            sequence: 1);

        Assert.That(store.EnsureLoaded().Projects["proj"].CommittedUnits, Is.EqualTo(200));
    }

    private YouTubeSettingsStore CreateSettingsStore()
    {
        var store = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, _settingsPath);
        store.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", DailyQuotaBudget = 10_000 });
        return store;
    }
}
