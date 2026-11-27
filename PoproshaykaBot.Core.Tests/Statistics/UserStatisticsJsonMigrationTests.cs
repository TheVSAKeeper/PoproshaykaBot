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

    [Test]
    public void DeserializeList_LegacyFormat_RoundTripsCleanlyIntoNewFormat()
    {
        const string LegacyListJson = """
                                      [
                                        {
                                          "userId": "107618204",
                                          "name": "thevsakeeper",
                                          "messageCount": 3351,
                                          "bonusMessageCount": 5000,
                                          "shtrafMessageCount": 217,
                                          "firstSeen": "2024-01-01T00:00:00Z",
                                          "lastSeen": "2024-02-01T00:00:00Z"
                                        }
                                      ]
                                      """;

        var loaded = JsonSerializer.Deserialize<List<UserStatistics>>(LegacyListJson, JsonStoreOptions.Default)!;
        var rewrittenJson = JsonSerializer.Serialize(loaded, JsonStoreOptions.Default);
        var reloaded = JsonSerializer.Deserialize<List<UserStatistics>>(rewrittenJson, JsonStoreOptions.Default)!;

        Assert.That(reloaded, Has.Count.EqualTo(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded[0].UserId, Is.EqualTo("107618204"));
            Assert.That(reloaded[0].Name, Is.EqualTo("thevsakeeper"));
            Assert.That(reloaded[0].MessageCount, Is.EqualTo(3351ul));
            Assert.That(reloaded[0].BonusPoints, Is.EqualTo(5000ul));
            Assert.That(reloaded[0].PenaltyPoints, Is.EqualTo(217ul));
            Assert.That(reloaded[0].Points, Is.EqualTo(3351L + 5000L - 217L));
            Assert.That(reloaded[0].FirstSeen, Is.EqualTo(loaded[0].FirstSeen));
            Assert.That(reloaded[0].LastSeen, Is.EqualTo(loaded[0].LastSeen));
            Assert.That(rewrittenJson, Does.Not.Contain("bonusMessageCount"));
            Assert.That(rewrittenJson, Does.Not.Contain("shtrafMessageCount"));
        }
    }
}
