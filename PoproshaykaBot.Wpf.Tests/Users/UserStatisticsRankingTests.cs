using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
public class UserStatisticsRankingTests
{
    private static readonly PointTerm Term = new();

    private static readonly List<UserRank> Ranks =
    [
        new("♟", "ПЕШКА", 0),
        new("♞", "КОНЬ", 300),
        new("♜", "ЛАДЬЯ", 1000),
    ];

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

        var current = Ranks
            .Where(rank => source.Points >= (long)rank.MinMessages)
            .OrderByDescending(rank => rank.MinMessages)
            .FirstOrDefault() ?? Ranks[0];

        var standing = UserRankStanding.Create(source.Points, $"{current.Emoji} {current.DisplayName}", current, Ranks);

        return new(source, standing, Term);
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
    [TestCase(UserStatisticsSortKey.Rank, true, "Anna,borland,zed")]
    [TestCase(UserStatisticsSortKey.Rank, false, "zed,borland,Anna")]
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

    [Test]
    public void Breaks_equal_ranks_by_points_and_then_by_name()
    {
        var rows = new List<UserStatisticsRowViewModel>
        {
            Row("bob", 10),
            Row("alice", 250),
            Row("carol", 250),
        };

        var ordered = UserStatisticsRanking.Arrange(rows, UserStatisticsSortKey.Rank, true);

        Assert.That(ordered.Select(row => row.Name), Is.EqualTo(new[] { "alice", "carol", "bob" }));
    }

    [Test]
    public void Measures_the_way_to_the_next_rank()
    {
        var row = Row("climber", 400);

        Assert.Multiple(() =>
        {
            Assert.That(row.RankDisplay, Is.EqualTo("♞ КОНЬ"));
            Assert.That(row.HasNextRank, Is.True);
            Assert.That(row.NextRankText, Is.EqualTo("До ранга ♜ ЛАДЬЯ"));
            Assert.That(row.Standing.PointsToNext, Is.EqualTo(600));
            Assert.That(row.Standing.Progress, Is.EqualTo(100d / 700).Within(0.0001));
        });
    }

    [Test]
    public void Says_the_top_rank_has_nowhere_left_to_grow()
    {
        var row = Row("king", 4000);

        Assert.Multiple(() =>
        {
            Assert.That(row.HasNextRank, Is.False);
            Assert.That(row.HasRankPath, Is.True);
            Assert.That(row.NextRankText, Is.EqualTo("Максимальный ранг"));
            Assert.That(row.PointsToNextText, Is.Empty);
        });
    }

    [Test]
    public void Shows_the_lowest_unreached_rank_as_the_way_not_as_the_chip()
    {
        List<UserRank> ranks = [new("♟", "ПЕШКА", 500)];

        var source = new UserStatistics
        {
            UserId = "newbie-id",
            Name = "newbie",
            MessageCount = 10,
        };

        var current = UserRank.Unranked;
        var standing = UserRankStanding.Create(source.Points, $"{current.Emoji} {current.DisplayName}", current, ranks);
        var row = new UserStatisticsRowViewModel(source, standing, Term);

        Assert.Multiple(() =>
        {
            Assert.That(row.RankDisplay, Is.EqualTo("🌱 БЕЗ РАНГА"));
            Assert.That(row.HasNextRank, Is.True);
            Assert.That(row.HasRankPath, Is.True);
            Assert.That(row.NextRankText, Is.EqualTo("До ранга ♟ ПЕШКА"));
            Assert.That(standing.PointsToNext, Is.EqualTo(490));
            Assert.That(standing.Progress, Is.EqualTo(10d / 500).Within(0.0001));
            Assert.That(row.RankOrder, Is.Zero);
        });
    }

    [Test]
    public void Shows_a_plain_zero_instead_of_a_signed_one()
    {
        var row = Row("quiet", 10);

        Assert.Multiple(() =>
        {
            Assert.That(row.BonusDisplay, Is.EqualTo("0"));
            Assert.That(row.PenaltyDisplay, Is.EqualTo("0"));
            Assert.That(row.HasBonus, Is.False);
            Assert.That(row.HasPenalty, Is.False);
        });
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
