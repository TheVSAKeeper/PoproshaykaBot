using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.Tests.YouTube.Quota;

[TestFixture]
public class PacificQuotaClockTests
{
    [Test]
    public void Pst_Day_Resets_At_08_Utc()
    {
        var window = PacificQuotaClock.GetWindow(new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(window.PacificDate, Is.EqualTo(new DateOnly(2026, 11, 15)));
            Assert.That(window.NextResetAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Exact_Pst_Midnight_Starts_New_Pacific_Date()
    {
        var justBefore = PacificQuotaClock.GetWindow(new DateTimeOffset(2026, 11, 16, 7, 59, 59, TimeSpan.Zero));
        var atMidnight = PacificQuotaClock.GetWindow(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(justBefore.PacificDate, Is.EqualTo(new DateOnly(2026, 11, 15)));
            Assert.That(atMidnight.PacificDate, Is.EqualTo(new DateOnly(2026, 11, 16)));
            Assert.That(atMidnight.NextResetAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Pdt_Day_Resets_At_07_Utc()
    {
        var window = PacificQuotaClock.GetWindow(new DateTimeOffset(2026, 7, 15, 7, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(window.PacificDate, Is.EqualTo(new DateOnly(2026, 7, 15)));
            Assert.That(window.NextResetAtUtc, Is.EqualTo(new DateTimeOffset(2026, 7, 16, 7, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Spring_Forward_Dst_Day_Lasts_23_Hours()
    {
        var start = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero);
        var window = PacificQuotaClock.GetWindow(start);

        Assert.Multiple(() =>
        {
            Assert.That(window.PacificDate, Is.EqualTo(new DateOnly(2026, 3, 8)));
            Assert.That(window.NextResetAtUtc, Is.EqualTo(new DateTimeOffset(2026, 3, 9, 7, 0, 0, TimeSpan.Zero)));
            Assert.That(window.NextResetAtUtc - start, Is.EqualTo(TimeSpan.FromHours(23)));
        });
    }

    [Test]
    public void Fall_Back_Dst_Day_Lasts_25_Hours()
    {
        var start = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
        var window = PacificQuotaClock.GetWindow(start);

        Assert.Multiple(() =>
        {
            Assert.That(window.PacificDate, Is.EqualTo(new DateOnly(2026, 11, 1)));
            Assert.That(window.NextResetAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero)));
            Assert.That(window.NextResetAtUtc - start, Is.EqualTo(TimeSpan.FromHours(25)));
        });
    }
}
