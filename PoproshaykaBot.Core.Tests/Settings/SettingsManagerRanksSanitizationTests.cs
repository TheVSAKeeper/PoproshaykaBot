using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;
using System.Text.Json;

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

    [TestCase("", "ТЕСТ", true)]
    [TestCase("★", "", true)]
    [TestCase("   ", "ТЕСТ", false)]
    [TestCase("★", "  ", false)]
    public void Load_RankWithEmptyEmojiOrName_RestoresDefaultsOnlyWhenEmpty(string emoji, string name, bool restored)
    {
        var json = $$"""
                     {
                       "ranks": {
                         "ranks": [
                           { "emoji": "♚", "name": "КОРОЛЬ", "minMessages": 5000, "tier": 0 },
                           { "emoji": "{{emoji}}", "name": "{{name}}", "minMessages": 0, "tier": 0 }
                         ]
                       }
                     }
                     """;

        var path = Path.Combine(Path.GetTempPath(), $"poproshayka-settings-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path);

            UserRank[] kept = [new("♚", "КОРОЛЬ", 5000), new(emoji, name, 0)];
            var expected = restored ? new RanksSettings().Ranks : [.. kept];

            Assert.That(manager.Current.Ranks.Ranks, Is.EqualTo(expected));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Load_FormerDefaultRanksWithWhiteKing_KeptWithoutUserAction()
    {
        UserRank[] former =
        [
            new("♔", "КОРОЛЬ", 5000),
            new("♛", "ФЕРЗЬ", 4000, 1),
            new("♛", "ФЕРЗЬ", 3500, 2),
            new("♛", "ФЕРЗЬ", 3000, 3),
            new("♜", "ЛАДЬЯ", 2500, 1),
            new("♜", "ЛАДЬЯ", 2000, 2),
            new("♜", "ЛАДЬЯ", 1500, 3),
            new("♝", "СЛОН", 1200, 1),
            new("♝", "СЛОН", 1000, 2),
            new("♝", "СЛОН", 800, 3),
            new("♞", "КОНЬ", 600, 1),
            new("♞", "КОНЬ", 450, 2),
            new("♞", "КОНЬ", 300, 3),
            new("♟", "ПЕШКА", 200, 1),
            new("♟", "ПЕШКА", 100, 2),
            new("♟", "ПЕШКА", 0, 3),
        ];

        var path = Path.Combine(Path.GetTempPath(), $"poproshayka-settings-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new AppSettings { Ranks = new() { Ranks = [.. former] } }, JsonStoreOptions.Default));

        try
        {
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path);

            Assert.That(manager.Current.Ranks.Ranks, Is.EqualTo(former));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
