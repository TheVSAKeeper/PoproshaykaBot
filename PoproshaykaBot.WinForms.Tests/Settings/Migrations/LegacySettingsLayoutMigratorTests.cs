using PoproshaykaBot.WinForms.Settings.Migrations;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Tests.Settings.Migrations;

[TestFixture]
public sealed class LegacySettingsLayoutMigratorTests
{
    private string _baseDirectory = null!;
    private string _settingsDirectory = null!;

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
                "Файл в settings/ имеет приоритет — мигратор не должен затирать актуальные данные");

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
    public void Run_NoLegacyFiles_DoesNothing()
    {
        LegacySettingsLayoutMigrator.Run(_baseDirectory, _settingsDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(_settingsDirectory), Is.True, "Целевая директория создаётся даже без работы");
            Assert.That(Directory.GetFiles(_settingsDirectory), Is.Empty);
        }
    }
}
