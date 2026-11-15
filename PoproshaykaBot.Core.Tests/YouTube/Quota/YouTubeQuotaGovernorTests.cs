using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.Tests.YouTube.Quota;

[TestFixture]
public class YouTubeQuotaGovernorTests
{
    private string _directory = null!;
    private TestTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeQuotaStore _store = null!;
    private YouTubeQuotaGovernor _governor = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-quota-gov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings
        {
            Enabled = true,
            ProjectId = "Project-One",
            ClientId = "client-1",
            DailyQuotaBudget = 10_000,
        });

        _store = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _governor = new YouTubeQuotaGovernor(_store, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Bot_And_Broadcaster_Share_Single_Project_Bucket_And_Split_Metrics()
    {
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 50, out var botLease), Is.True);
        botLease.Commit();

        Assert.That(_governor.TryReserve(PlatformAuthRole.Broadcaster, YouTubeQuotaPriority.CriticalReadAndMonitor, 10, out var broadcasterLease), Is.True);
        broadcasterLease.Commit();

        var status = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(status.ProjectKey, Is.EqualTo("project-one"));
            Assert.That(status.CommittedUnits, Is.EqualTo(60));
            Assert.That(status.BotCommittedUnits, Is.EqualTo(50));
            Assert.That(status.BroadcasterCommittedUnits, Is.EqualTo(10));
            Assert.That(status.ReservedUnits, Is.EqualTo(0));
            Assert.That(status.Tier1RemainingUnits, Is.EqualTo(9_940));
        });
    }

    [Test]
    public void Changing_ProjectId_Isolates_Buckets()
    {
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 100, out var lease), Is.True);
        lease.Commit();

        _settingsStore.Mutate(s => s.ProjectId = "Project-Two");
        var secondStatus = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(secondStatus.ProjectKey, Is.EqualTo("project-two"));
            Assert.That(secondStatus.CommittedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void Lower_Priorities_Enforce_Reserve_Floors_While_Tier1_Keeps_Remaining_Floor()
    {
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.ScheduledBroadcast, 5_500, out var tier4Ok), Is.True);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.ScheduledBroadcast, 1, out _), Is.False);
        tier4Ok.Commit();

        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.StandaloneGreeting, 1_500, out var tier3Ok), Is.True);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.StandaloneGreeting, 1, out _), Is.False);
        tier3Ok.Commit();

        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CommandReply, 1_000, out var tier2Ok), Is.True);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CommandReply, 1, out _), Is.False);
        tier2Ok.Commit();

        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 2_000, out var tier1Ok), Is.True);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out _), Is.False);
        tier1Ok.Commit();

        Assert.That(_governor.CaptureDiagnostics().Tier1RemainingUnits, Is.EqualTo(0));
    }

    [Test]
    public void Custom_DailyBudget_Scales_Ceilings_Proportionally()
    {
        _settingsStore.Mutate(s => s.DailyQuotaBudget = 2_000);

        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.ScheduledBroadcast, 1_100, out var ok), Is.True);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.ScheduledBroadcast, 1, out _), Is.False);
        ok.Dispose();
    }

    [Test]
    public void Lease_Dispose_Without_Commit_Refunds_Reserved_Units()
    {
        using (var lease = ReserveOrFail(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 50))
        {
            Assert.That(_governor.CaptureDiagnostics().ReservedUnits, Is.EqualTo(50));
        }

        var status = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(status.ReservedUnits, Is.EqualTo(0));
            Assert.That(status.CommittedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void Commit_Across_Pacific_Midnight_Does_Not_Underflow_New_Day_Reserved_Units()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 11, 16, 7, 59, 55, TimeSpan.Zero);
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var inFlight), Is.True);

        _clock.UtcNow = new DateTimeOffset(2026, 11, 16, 8, 0, 5, TimeSpan.Zero);
        inFlight.Commit();

        var status = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(status.PacificDate, Is.EqualTo(new DateOnly(2026, 11, 16)));
            Assert.That(status.CommittedUnits, Is.EqualTo(5));
            Assert.That(status.ReservedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void MarkExhaustedFromServer_Clamps_Until_Pacific_Midnight_Rollover()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);
        _governor.MarkExhaustedFromServer(PlatformAuthRole.Bot);

        Assert.Multiple(() =>
        {
            Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out _), Is.False);
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.True);
        });

        _clock.UtcNow = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.False);
            Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 5, out var lease), Is.True);
            lease.Dispose();
        });
    }

    private YouTubeQuotaLease ReserveOrFail(PlatformAuthRole role, YouTubeQuotaPriority priority, int cost)
    {
        Assert.That(_governor.TryReserve(role, priority, cost, out var lease), Is.True);
        return lease;
    }
}
