using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class UserStatisticsRankingTests
{
    private static readonly PointTerm Term = new();

    private static UserStatisticsRowViewModel Row(string name, ulong messages, ulong bonus = 0, ulong penalty = 0)
    {
        var source = new UserStatistics
        {
            UserId = name + "-id",
            Name = name,
            MessageCount = messages,
            BonusPoints = bonus,
            PenaltyPoints = penalty,
        };

        return new(source, "♟ ПЕШКА", Term);
    }

    private static List<UserStatisticsRowViewModel> Sample()
    {
        return
        [
            Row("borland", 100),
            Row("Anna", 300, bonus: 200),
            Row("zed", 50, penalty: 500),
        ];
    }

    [TestCase(UserStatisticsSortKey.Points, true, "Anna,borland,zed")]
    [TestCase(UserStatisticsSortKey.Points, false, "zed,borland,Anna")]
    [TestCase(UserStatisticsSortKey.Messages, true, "Anna,borland,zed")]
    [TestCase(UserStatisticsSortKey.Messages, false, "zed,borland,Anna")]
    [TestCase(UserStatisticsSortKey.Name, false, "Anna,borland,zed")]
    [TestCase(UserStatisticsSortKey.Name, true, "zed,borland,Anna")]
    public void Orders_rows_by_key_and_direction(UserStatisticsSortKey sortKey, bool descending, string expected)
    {
        var ordered = UserStatisticsRanking.Arrange(Sample(), sortKey, descending);

        Assert.That(string.Join(',', ordered.Select(row => row.Name)), Is.EqualTo(expected));
    }

    [Test]
    public void Numbers_positions_from_one_in_display_order()
    {
        var ordered = UserStatisticsRanking.Arrange(Sample(), UserStatisticsSortKey.Name, false);

        Assert.That(ordered.Select(row => row.Position), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Shares_are_measured_against_the_richest_row_of_the_set()
    {
        var ordered = UserStatisticsRanking.Arrange(Sample(), UserStatisticsSortKey.Name, false);

        Assert.Multiple(() =>
        {
            Assert.That(ordered[0].Share, Is.EqualTo(1).Within(0.0001));
            Assert.That(ordered[1].Share, Is.EqualTo(0.2).Within(0.0001));
            Assert.That(ordered[2].Share, Is.EqualTo(UserStatisticsRanking.MinimumShare));
        });
    }

    [Test]
    public void Recounts_positions_and_shares_after_filtering()
    {
        var filtered = Sample().Where(row => row.Name != "Anna").ToList();

        var ordered = UserStatisticsRanking.Arrange(filtered, UserStatisticsSortKey.Points, true);

        Assert.Multiple(() =>
        {
            Assert.That(ordered.Select(row => row.Name), Is.EqualTo(new[] { "borland", "zed" }));
            Assert.That(ordered.Select(row => row.Position), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ordered[0].Share, Is.EqualTo(1).Within(0.0001));
        });
    }

    [Test]
    public void Keeps_the_bar_visible_when_nobody_has_points()
    {
        var ordered = UserStatisticsRanking.Arrange([Row("empty", 0)], UserStatisticsSortKey.Points, true);

        Assert.That(ordered[0].Share, Is.EqualTo(UserStatisticsRanking.MinimumShare));
    }

    [TestCase(1, 421, "1-е место из 421")]
    [TestCase(12, 12, "12-е место из 12")]
    [TestCase(0, 421, "")]
    [TestCase(3, 0, "")]
    public void Names_the_place_of_a_row_in_the_visible_set(int position, int total, string expected)
    {
        Assert.That(UserStatisticsRanking.DescribePlace(position, total), Is.EqualTo(expected));
    }

    [Test]
    public void Counts_what_the_filter_left_on_screen()
    {
        Assert.That(UserStatisticsRanking.DescribeVisibleCount(12, 421), Is.EqualTo("Показано 12 из 421"));
    }

    [Test]
    public void Marks_only_the_first_three_rows_as_leaders()
    {
        var rows = new List<UserStatisticsRowViewModel>
        {
            Row("a", 400),
            Row("b", 300),
            Row("c", 200),
            Row("d", 100),
        };

        var ordered = UserStatisticsRanking.Arrange(rows, UserStatisticsSortKey.Points, true);

        Assert.That(ordered.Select(row => row.IsTopThree), Is.EqualTo(new[] { true, true, true, false }));
    }
}
