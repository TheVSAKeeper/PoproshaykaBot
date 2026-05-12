using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class LegacyUsersStatisticsLoadTest
{
    private const string LegacyFilePath = @"\old\users_statistics.json";

    [Test]
    public void ProductionLegacyFile_DeserializesAndMigratesCleanly()
    {
        if (!File.Exists(LegacyFilePath))
        {
            Assert.Ignore($"Legacy file not found at {LegacyFilePath}");
        }

        var json = File.ReadAllText(LegacyFilePath);
        var users = JsonSerializer.Deserialize<List<UserStatistics>>(json, JsonStoreOptions.Default);

        Assert.That(users, Is.Not.Null);
        Assert.That(users!, Is.Not.Empty);

        var thevsakeeper = users.FirstOrDefault(u => u.UserId == "107618204");
        Assert.That(thevsakeeper, Is.Not.Null, "Expected user 107618204 (thevsakeeper) in legacy file");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(thevsakeeper!.Name, Is.EqualTo("thevsakeeper"));
            Assert.That(thevsakeeper.MessageCount, Is.EqualTo(3351ul));
            Assert.That(thevsakeeper.PenaltyPoints, Is.EqualTo(217ul), "shtrafMessageCount should map to PenaltyPoints");
            Assert.That(thevsakeeper.BonusPoints, Is.EqualTo(5000ul), "bonusMessageCount should map to BonusPoints");
            Assert.That(thevsakeeper.Points, Is.EqualTo(3351L + 5000L - 217L));
        }

        Assert.That(users.All(u => u.MessageCount >= 0), Is.True);
        Assert.That(users.All(u => !string.IsNullOrEmpty(u.UserId)), Is.True);
        Assert.That(users.All(u => !string.IsNullOrEmpty(u.Name)), Is.True);
    }

    [Test]
    public void ProductionLegacyFile_RoundTripsCleanlyIntoNewFormat()
    {
        if (!File.Exists(LegacyFilePath))
        {
            Assert.Ignore($"Legacy file not found at {LegacyFilePath}");
        }

        var legacyJson = File.ReadAllText(LegacyFilePath);
        var loaded = JsonSerializer.Deserialize<List<UserStatistics>>(legacyJson, JsonStoreOptions.Default)!;

        var rewrittenJson = JsonSerializer.Serialize(loaded, JsonStoreOptions.Default);
        var reloaded = JsonSerializer.Deserialize<List<UserStatistics>>(rewrittenJson, JsonStoreOptions.Default)!;

        Assert.That(reloaded, Has.Count.EqualTo(loaded.Count));

        for (var i = 0; i < loaded.Count; i++)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reloaded[i].UserId, Is.EqualTo(loaded[i].UserId));
                Assert.That(reloaded[i].Name, Is.EqualTo(loaded[i].Name));
                Assert.That(reloaded[i].MessageCount, Is.EqualTo(loaded[i].MessageCount));
                Assert.That(reloaded[i].BonusPoints, Is.EqualTo(loaded[i].BonusPoints));
                Assert.That(reloaded[i].PenaltyPoints, Is.EqualTo(loaded[i].PenaltyPoints));
                Assert.That(reloaded[i].Points, Is.EqualTo(loaded[i].Points));
                Assert.That(reloaded[i].FirstSeen, Is.EqualTo(loaded[i].FirstSeen));
                Assert.That(reloaded[i].LastSeen, Is.EqualTo(loaded[i].LastSeen));
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rewrittenJson, Does.Not.Contain("bonusMessageCount"), "Rewritten JSON should not contain legacy field");
            Assert.That(rewrittenJson, Does.Not.Contain("shtrafMessageCount"), "Rewritten JSON should not contain legacy field");
            Assert.That(rewrittenJson, Does.Contain("bonusPoints"));
            Assert.That(rewrittenJson, Does.Contain("penaltyPoints"));
        }
    }
}
