using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Tests.Settings.Migrations;

[TestFixture]
public sealed class LegacySettingsLayoutMigratorTests
{
    [SetUp]
    public void SetUp()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "poproshayka-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
        _settingsDirectory = Path.Combine(_baseDirectory, "settings");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_baseDirectory, true);
        }
        catch
        {
        }
    }

    private string _baseDirectory = null!;
    private string _settingsDirectory = null!;

    private const string MonolithicWithTokens = """
                                                {
                                                  "twitch": {
                                                    "channel": "bobito217",
                                                    "botAccount": {
                                                      "login": "thebot",
                                                      "accessToken": "bot-secret-access",
                                                      "refreshToken": "bot-secret-refresh"
                                                    }
                                                  },
                                                  "ui": {
                                                    "dashboard": { "columnCount": 4 }
                                                  }
                                                }
                                                """;

    [Test]
    public void Run_RelocatesFlatSettingsFilesIntoSubdirectory()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "accounts.json"), "{}");
        File.WriteAllText(Path.Combine(_baseDirectory, "broadcast-profiles.json"), "{}");
        File.WriteAllText(Path.Combine(_baseDirectory, "polls.json"), "{}");
        File.WriteAllText(Path.Combine(_baseDirectory, "obs-chat.json"), "{}");
        File.WriteAllText(Path.Combine(_baseDirectory, "recent-categories.json"), "{}");
        File.WriteAllText(Path.Combine(_baseDirectory, "dashboard-layout.json"), "{}");

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "accounts.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "broadcast-profiles.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "polls.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "obs-chat.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "recent-categories.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "dashboard-layout.json")), Is.True);

            Assert.That(File.Exists(Path.Combine(_baseDirectory, "accounts.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_baseDirectory, "broadcast-profiles.json")), Is.False);
        }
    }

    [Test]
    public void Run_PreservesLegacyOriginalAsTimestampedBackup()
    {
        const string Original = "{\"original\":true}";
        File.WriteAllText(Path.Combine(_baseDirectory, "accounts.json"), Original);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        var backups = Directory.GetFiles(_baseDirectory, "accounts.legacy-*.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_baseDirectory, "accounts.json")), Is.False,
                "Канонический legacy-путь освобождается, чтобы повторный запуск не зацикливался");

            Assert.That(backups, Has.Length.EqualTo(1), "Должен остаться ровно один таймстампированный бэкап оригинала");
            Assert.That(File.ReadAllText(backups[0]), Is.EqualTo(Original));
            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "accounts.json")), Is.EqualTo(Original));
        }
    }

    [Test]
    public void Run_DoesNotOverwriteExistingTargetFile()
    {
        Directory.CreateDirectory(_settingsDirectory);
        File.WriteAllText(Path.Combine(_baseDirectory, "accounts.json"), "{\"legacy\":true}");
        File.WriteAllText(Path.Combine(_settingsDirectory, "accounts.json"), "{\"current\":true}");

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "accounts.json")),
                Is.EqualTo("{\"current\":true}"),
                "Файл в settings/ имеет приоритет – мигратор не должен затирать актуальные данные");

            Assert.That(File.Exists(Path.Combine(_baseDirectory, "accounts.json")), Is.True,
                "Legacy-копия остаётся на месте, чтобы пользователь мог разобраться вручную");
        }
    }

    [Test]
    public void Run_SplitsMonolithicSettingsIntoSeparateFiles()
    {
        const string Monolithic = """
                                  {
                                    "twitch": {
                                      "channel": "bobito217",
                                      "botAccount": { "login": "thebot" },
                                      "broadcasterAccount": { "login": "thecaster" },
                                      "broadcastProfiles": { "profiles": [], "lastAppliedProfileId": null },
                                      "polls": { "profiles": [], "historyMaxItems": 500 },
                                      "obsChat": { "fontSize": 24 },
                                      "infrastructure": { "chatHistoryMaxItems": 1000, "recentCategories": [] }
                                    },
                                    "ui": {
                                      "dashboard": { "columnCount": 4 },
                                      "mainWindow": { "width": 1280 }
                                    }
                                  }
                                  """;

        File.WriteAllText(Path.Combine(_baseDirectory, "settings.json"), Monolithic);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "settings.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "accounts.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "broadcast-profiles.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "polls.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "obs-chat.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "recent-categories.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_settingsDirectory, "dashboard-layout.json")), Is.True);

            var migratedSettings = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json"));
            Assert.That(migratedSettings, Does.Not.Contain("\"botAccount\""));
            Assert.That(migratedSettings, Does.Not.Contain("\"broadcastProfiles\""));
            Assert.That(migratedSettings, Does.Not.Contain("\"polls\""));
            Assert.That(migratedSettings, Does.Not.Contain("\"obsChat\""));
            Assert.That(migratedSettings, Does.Not.Contain("\"dashboard\""));
            Assert.That(migratedSettings, Does.Contain("\"channel\""));
        }
    }

    [Test]
    public void Run_PicksUpStoredAccountsAfterMigration()
    {
        const string Monolithic = """
                                  {
                                    "twitch": {
                                      "botAccount": { "login": "thebot", "userId": "42" },
                                      "broadcasterAccount": { "login": "thecaster", "userId": "100" }
                                    }
                                  }
                                  """;

        File.WriteAllText(Path.Combine(_baseDirectory, "settings.json"), Monolithic);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        var accountsStore = new AccountsStore(filePath: Path.Combine(_settingsDirectory, "accounts.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(accountsStore.LoadBot().Login, Is.EqualTo("thebot"));
            Assert.That(accountsStore.LoadBot().UserId, Is.EqualTo("42"));
            Assert.That(accountsStore.LoadBroadcaster().Login, Is.EqualTo("thecaster"));
            Assert.That(accountsStore.LoadBroadcaster().UserId, Is.EqualTo("100"));
        }
    }

    [Test]
    public void Run_IsIdempotent()
    {
        const string Monolithic = """
                                  {
                                    "twitch": {
                                      "channel": "bobito217",
                                      "botAccount": { "login": "thebot" }
                                    }
                                  }
                                  """;

        File.WriteAllText(Path.Combine(_baseDirectory, "settings.json"), Monolithic);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);
        var firstAccounts = File.ReadAllText(Path.Combine(_settingsDirectory, "accounts.json"));
        var firstSettings = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json"));

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "accounts.json")), Is.EqualTo(firstAccounts));
            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json")), Is.EqualTo(firstSettings));
        }
    }

    [Test]
    public void Run_SplitsOnlyInsideTheSettingsDirectory_AndLeavesNothingForSettingsManager()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "settings.json"), MonolithicWithTokens);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        var settingsFile = Path.Combine(_settingsDirectory, "settings.json");
        var afterMigrator = File.ReadAllText(settingsFile);

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsFile).Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(_baseDirectory, "*.json"), Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(_baseDirectory, "settings.legacy-*.json"), Has.Length.EqualTo(1),
                "Сплиттер обязан работать по settings/, а не по корню: порядок «перенос, затем разбор» держится на этом.");

            Assert.That(File.ReadAllText(settingsFile), Is.EqualTo(afterMigrator),
                "SettingsManager поднимается после DI-сборки и по уже разобранному файлу не должен писать ничего.");

            Assert.That(Directory.GetFiles(_settingsDirectory, "settings.pre-migration-*.json"), Has.Length.EqualTo(1),
                "Второй pre-migration бэкап означал бы, что разбор монолита прошёл дважды.");

            Assert.That(settings.Twitch.Channel, Is.EqualTo("bobito217"));
        }
    }

    [Test]
    public void Run_MonolithicSettingsWithTokens_KeepsThemOnlyInAccountsFile()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "settings.json"), MonolithicWithTokens);

        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        var backups = Directory.GetFiles(_settingsDirectory, "settings.pre-migration-*.json");
        var backup = backups.Length == 1 ? File.ReadAllText(backups[0]) : string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(backup, Is.EqualTo(RedactedMonolith()),
                "Бэкап – это монолит с вычеркнутыми токенами: канал, раскладка и остальное обязаны дойти до разбора целиком.");

            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "accounts.json")), Does.Contain("bot-secret-access"),
                "Редактируется только бэкап – рабочий accounts.json обязан унести токены дальше.");

            Assert.That(File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json")), Does.Not.Contain("bot-secret-access"));
        }
    }

    [Test]
    public void Run_NoLegacyFiles_DoesNothing()
    {
        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(_settingsDirectory), Is.True, "Целевая директория создаётся даже без работы");
            Assert.That(Directory.GetFiles(_settingsDirectory), Is.Empty);
        }
    }

    private static string RedactedMonolith()
    {
        var expected = JsonNode.Parse(MonolithicWithTokens)!;
        var account = expected["twitch"]!["botAccount"]!;

        account["accessToken"] = string.Empty;
        account["refreshToken"] = string.Empty;

        return expected.ToJsonString(JsonStoreOptions.Default);
    }
}
