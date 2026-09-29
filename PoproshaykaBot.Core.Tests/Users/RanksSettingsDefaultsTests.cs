using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Users;

[TestFixture]
public sealed class RanksSettingsDefaultsTests
{
    private static readonly ulong[] FormerThresholds = [5000, 4000, 3500, 3000, 2500, 2000, 1500, 1200, 1000, 800, 600, 450, 300, 200, 100, 0];

    [Test]
    public void Defaults_NeverRaiseThresholdOfSameStep()
    {
        var ranks = new RanksSettings().Ranks;

        Assert.That(ranks, Has.Count.EqualTo(FormerThresholds.Length));

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < ranks.Count; i++)
            {
                Assert.That(ranks[i].MinMessages, Is.LessThanOrEqualTo(FormerThresholds[i]), ranks[i].DisplayName);
            }
        }
    }

    [Test]
    public void Defaults_AreStrictlyDescendingAndStartAtZero()
    {
        var thresholds = new RanksSettings().Ranks.Select(rank => rank.MinMessages).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(thresholds, Is.Ordered.Descending);
            Assert.That(thresholds, Is.Unique);
            Assert.That(thresholds[^1], Is.Zero);
            Assert.That(new RanksSettings().Ranks.All(rank => UserRankRules.DescribeEmojiError(rank.Emoji) is null && UserRankRules.DescribeNameError(rank.Name) is null), Is.True);
        }
    }
}
