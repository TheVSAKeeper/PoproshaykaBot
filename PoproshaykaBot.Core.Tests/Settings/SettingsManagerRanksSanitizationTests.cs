using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class SettingsManagerRanksSanitizationTests
{
    [Test]
    public void Load_CorruptedRanksWithNullEmojiAndName_RestoresDefaults()
    {
        const string CorruptedJson = """
                                     {
                                       "ranks": {
                                         "pointTerm": {
                                           "singular": "монета",
                                           "few": "монеты",
                                           "many": "монет"
                                         },
                                         "ranks": [
                                           { "emoji": null, "name": null, "minMessages": 0, "tier": 0 },
                                           { "emoji": null, "name": null, "minMessages": 0, "tier": 0 }
                                         ]
                                       }
                                     }
                                     """;

        var path = Path.Combine(Path.GetTempPath(), $"poproshayka-settings-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, CorruptedJson);

        try
        {
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path);
            var current = manager.Current;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(current.Ranks.Ranks, Has.Count.GreaterThan(0));
                Assert.That(current.Ranks.Ranks.All(r => !string.IsNullOrEmpty(r.Emoji)), Is.True);
                Assert.That(current.Ranks.Ranks.All(r => !string.IsNullOrEmpty(r.Name)), Is.True);
                Assert.That(current.Ranks.Ranks.Any(r => r.Name == "КОРОЛЬ"), Is.True);
                Assert.That(current.Ranks.PointTerm.Singular, Is.EqualTo("монета"), "PointTerm must be preserved when only ranks are sanitized");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Load_ValidRanks_PreservedAsIs()
    {
        const string ValidJson = """
                                 {
                                   "ranks": {
                                     "ranks": [
                                       { "emoji": "★", "name": "ТЕСТ", "minMessages": 1234, "tier": 0 }
                                     ]
                                   }
                                 }
                                 """;

        var path = Path.Combine(Path.GetTempPath(), $"poproshayka-settings-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ValidJson);

        try
        {
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path);
            var current = manager.Current;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(current.Ranks.Ranks, Has.Count.EqualTo(1));
                Assert.That(current.Ranks.Ranks[0].Emoji, Is.EqualTo("★"));
                Assert.That(current.Ranks.Ranks[0].Name, Is.EqualTo("ТЕСТ"));
                Assert.That(current.Ranks.Ranks[0].MinMessages, Is.EqualTo(1234ul));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
