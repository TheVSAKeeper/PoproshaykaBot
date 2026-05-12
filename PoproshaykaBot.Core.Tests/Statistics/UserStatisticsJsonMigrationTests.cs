using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class UserStatisticsJsonMigrationTests
{
    [Test]
    public void Deserialize_LegacyBonusAndShtrafMessageCount_MigratesToPointsFields()
    {
        const string LegacyJson = """
                                  {
                                    "userId": "u1",
                                    "name": "Alice",
                                    "messageCount": 100,
                                    "bonusMessageCount": 25,
                                    "shtrafMessageCount": 7,
                                    "firstSeen": "2024-01-01T00:00:00Z",
                                    "lastSeen": "2024-01-02T00:00:00Z"
                                  }
                                  """;

        var stats = JsonSerializer.Deserialize<UserStatistics>(LegacyJson, JsonStoreOptions.Default);

        Assert.That(stats, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats!.MessageCount, Is.EqualTo(100ul));
            Assert.That(stats.BonusPoints, Is.EqualTo(25ul));
            Assert.That(stats.PenaltyPoints, Is.EqualTo(7ul));
            Assert.That(stats.Points, Is.EqualTo(118));
        }
    }

    [Test]
    public void Deserialize_NewBonusPointsAndPenaltyPoints_LoadsAsIs()
    {
        const string NewJson = """
                               {
                                 "userId": "u1",
                                 "name": "Alice",
                                 "messageCount": 10,
                                 "bonusPoints": 5,
                                 "penaltyPoints": 2,
                                 "firstSeen": "2024-01-01T00:00:00Z",
                                 "lastSeen": "2024-01-02T00:00:00Z"
                               }
                               """;

        var stats = JsonSerializer.Deserialize<UserStatistics>(NewJson, JsonStoreOptions.Default);

        Assert.That(stats, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats!.BonusPoints, Is.EqualTo(5ul));
            Assert.That(stats.PenaltyPoints, Is.EqualTo(2ul));
            Assert.That(stats.Points, Is.EqualTo(13));
        }
    }

    [Test]
    public void Serialize_DoesNotWriteLegacyFields()
    {
        var stats = new UserStatistics
        {
            UserId = "u1",
            Name = "Alice",
            MessageCount = 10,
            BonusPoints = 5,
            PenaltyPoints = 2,
        };

        var json = JsonSerializer.Serialize(stats, JsonStoreOptions.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Not.Contain("bonusMessageCount"));
            Assert.That(json, Does.Not.Contain("shtrafMessageCount"));
            Assert.That(json, Does.Contain("bonusPoints"));
            Assert.That(json, Does.Contain("penaltyPoints"));
        }
    }
}
