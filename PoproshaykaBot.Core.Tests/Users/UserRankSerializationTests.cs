using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Users;

[TestFixture]
public sealed class UserRankSerializationTests
{
    [Test]
    public void Serialize_DefaultRanks_PreservesEmojiAndNameAndMinMessages()
    {
        var ranks = new RanksSettings();
        var json = JsonSerializer.Serialize(ranks, JsonStoreOptions.Default);

        TestContext.Out.WriteLine(json);

        Assert.That(json, Does.Contain("\"♔\""), "King emoji must be preserved");
        Assert.That(json, Does.Contain("КОРОЛЬ"));
        Assert.That(json, Does.Contain("\"minMessages\": 5000"));
    }

    [Test]
    public void RoundTrip_DefaultRanksSettings_PreservesAllRankData()
    {
        var ranks = new RanksSettings();
        var json = JsonSerializer.Serialize(ranks, JsonStoreOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<RanksSettings>(json, JsonStoreOptions.Default);

        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.Ranks, Has.Count.EqualTo(ranks.Ranks.Count));

        for (var i = 0; i < ranks.Ranks.Count; i++)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(roundTripped.Ranks[i].Emoji, Is.EqualTo(ranks.Ranks[i].Emoji), $"Emoji mismatch at index {i}");
                Assert.That(roundTripped.Ranks[i].Name, Is.EqualTo(ranks.Ranks[i].Name), $"Name mismatch at index {i}");
                Assert.That(roundTripped.Ranks[i].MinMessages, Is.EqualTo(ranks.Ranks[i].MinMessages), $"MinMessages mismatch at index {i}");
                Assert.That(roundTripped.Ranks[i].Tier, Is.EqualTo(ranks.Ranks[i].Tier), $"Tier mismatch at index {i}");
            }
        }
    }
}
