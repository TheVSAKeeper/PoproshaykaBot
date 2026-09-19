using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class StreamHistoryRowTests
{
    private static readonly DateTimeOffset Start = new(new DateTime(2026, 9, 6, 17, 37, 0, DateTimeKind.Local));

    private static StreamSessionRowViewModel Row(int peakViewers, string? title = "эфир")
    {
        return new(new()
        {
            StartedAt = Start,
            EndedAt = Start.AddHours(3),
            Title = title,
            PeakViewers = peakViewers,
        });
    }

    [TestCase(131, 100, 5, "+31 % к обычному", TrendTone.Up)]
    [TestCase(60, 100, 5, "−40 % к обычному", TrendTone.Down)]
    [TestCase(102, 100, 5, "как обычно", TrendTone.Flat)]
    [TestCase(96, 100, 5, "как обычно", TrendTone.Flat)]
    [TestCase(95, 100, 5, "−5 % к обычному", TrendTone.Down)]
    [TestCase(131, 100, 1, "", TrendTone.None)]
    [TestCase(131, 0, 5, "", TrendTone.None)]
    public void Compares_a_session_with_the_average(
        double value,
        double average,
        int sessionCount,
        string expectedText,
        TrendTone expectedTone)
    {
        var (text, tone) = StreamHistoryPageViewModel.DescribeDelta(value, average, sessionCount);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(expectedText));
            Assert.That(tone, Is.EqualTo(expectedTone));
        });
    }

    [TestCase(20, 20, 1d)]
    [TestCase(10, 20, 0.5)]
    [TestCase(0, 20, StreamTrendBarViewModel.MinimumShare)]
    [TestCase(7, 0, StreamTrendBarViewModel.MinimumShare)]
    public void Scales_a_trend_bar_against_the_peak_leader(int peakViewers, int leader, double expectedShare)
    {
        var bar = new StreamTrendBarViewModel(Row(peakViewers), leader);

        Assert.Multiple(() =>
        {
            Assert.That(bar.Share, Is.EqualTo(expectedShare).Within(1e-9));
            Assert.That(bar.FillTrack.Value + bar.RestTrack.Value, Is.EqualTo(1).Within(1e-9));
            Assert.That(bar.Label, Does.StartWith("06.09.2026"));
        });
    }

    [TestCase(1, 54, 54, 1d, true)]
    [TestCase(3, 27, 54, 0.5, true)]
    [TestCase(4, 0, 54, UserStatisticsRanking.MinimumShare, false)]
    public void Ranks_a_chatter_against_the_loudest_one(
        int position,
        long messageCount,
        long leader,
        double expectedShare,
        bool expectedTopThree)
    {
        var row = new StreamSessionChatterRowViewModel(
            new() { UserId = "42", DisplayName = "qp_illson", MessageCount = messageCount },
            position,
            leader);

        Assert.Multiple(() =>
        {
            Assert.That(row.Share, Is.EqualTo(expectedShare).Within(1e-9));
            Assert.That(row.IsTopThree, Is.EqualTo(expectedTopThree));
            Assert.That(row.Summary, Does.Contain("qp_illson"));
        });
    }

    [TestCase("эфир", "эфир", false)]
    [TestCase(null, "Без названия", true)]
    [TestCase("", "Без названия", true)]
    public void Names_a_session_without_a_title(string? title, string expected, bool expectedMissing)
    {
        var row = Row(10, title);

        Assert.Multiple(() =>
        {
            Assert.That(row.TitleFormatted, Is.EqualTo(expected));
            Assert.That(row.HasTitle, Is.EqualTo(!expectedMissing));
        });
    }

    [Test]
    public void Spells_a_period_inside_one_day_without_repeating_the_date()
    {
        var period = StreamHistoryPageViewModel.FormatPeriod(Start, Start.AddHours(3).AddMinutes(42));

        Assert.That(period, Is.EqualTo("06.09.2026, 17:37 – 21:19"));
    }
}
