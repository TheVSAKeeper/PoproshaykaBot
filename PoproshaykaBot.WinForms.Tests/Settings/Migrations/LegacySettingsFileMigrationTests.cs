using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Migrations;
using PoproshaykaBot.WinForms.Twitch.Chat;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.WinForms.Tests.Settings.Migrations;

[TestFixture]
[Explicit("Ручной прогон: читает реальный файл old/settings.json из корня репозитория и проверяет, что миграция корректно поднимает его до актуальной схемы.")]
public sealed class LegacySettingsFileMigrationTests
{
    private static string ResolveLegacySettingsPath([CallerFilePath] string callerPath = "")
    {
        // .../PoproshaykaBot.WinForms.Tests/Settings/Migrations/<этот файл>.cs → ../../..  → корень репозитория
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", ".."));
        return Path.Combine(repoRoot, "old", "settings.json");
    }

    private static JsonObject LoadLegacyRoot()
    {
        var path = ResolveLegacySettingsPath();
        Assert.That(File.Exists(path), Is.True, $"Файл не найден: {path}. Положи реальный legacy-конфиг в old/settings.json и запусти тест явно.");

        var json = File.ReadAllText(path);
        return JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidOperationException("Корневой элемент old/settings.json не является JSON-объектом");
    }

    [Test]
    public void OldSettingsJson_IsMigratedAndLegacyFieldsAreRemoved()
    {
        var root = LoadLegacyRoot();

        var changed = SettingsMigrator.TryMigrate(root);

        var twitch = root["twitch"] as JsonObject;
        Assert.That(twitch, Is.Not.Null, "В старом конфиге обязана быть секция twitch");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True, "Старый файл должен поменяться после миграции");

            Assert.That(twitch!.ContainsKey("botUsername"), Is.False, "twitch.botUsername должен переехать в botAccount.login");
            Assert.That(twitch.ContainsKey("accessToken"), Is.False);
            Assert.That(twitch.ContainsKey("refreshToken"), Is.False);
            Assert.That(twitch.ContainsKey("scopes"), Is.False);
            Assert.That(twitch.ContainsKey("storedScopes"), Is.False);

            if (root["ui"] is JsonObject ui)
            {
                Assert.That(ui.ContainsKey("showLogsPanel"), Is.False);
                Assert.That(ui.ContainsKey("showChatPanel"), Is.False);
                Assert.That(ui.ContainsKey("currentChatViewMode"), Is.False);
            }
        }
    }

    [Test]
    public void OldSettingsJson_DeserializesIntoCurrentAppSettings()
    {
        var root = LoadLegacyRoot();

        SettingsMigrator.TryMigrate(root);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = root.Deserialize<AppSettings>(options);

        Assert.That(settings, Is.Not.Null, "Мигрированный JSON должен валидно десериализоваться в AppSettings");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings!.Twitch.Channel, Is.Not.Empty, "Имя канала из legacy-конфига должно сохраниться");
            Assert.That(settings.Twitch.ClientId, Is.Not.Empty, "ClientId должен сохраниться");
            Assert.That(settings.Twitch.RedirectUri, Is.Not.Empty);

            Assert.That(settings.Twitch.BotAccount.Login, Is.Not.Null, "botAccount.login обязан существовать (даже пустой), а не быть null");

            Assert.That(settings.Ranks.Ranks, Is.Not.Empty, "Ранги должны выживать миграцию");
            Assert.That(settings.Twitch.Messages.Welcome, Is.Not.Empty, "Кастомные сообщения должны сохраниться");
            Assert.That(settings.Twitch.ObsChat.MaxMessages, Is.GreaterThan(0));
        }
    }

    [Test]
    public void OldSettingsJson_TokensAndScopesAreFullyResetAfterMigration()
    {
        var root = LoadLegacyRoot();

        var twitchBefore = root["twitch"]!.AsObject();
        Assume.That(twitchBefore.ContainsKey("accessToken")
                    || twitchBefore.ContainsKey("refreshToken")
                    || twitchBefore.ContainsKey("scopes")
                    || twitchBefore.ContainsKey("storedScopes"),
            Is.True,
            "Тест имеет смысл только если в legacy-файле реально были accessToken/refreshToken/scopes/storedScopes");

        var legacyScopes = (twitchBefore["scopes"] as JsonArray)
                           ?.Select(node => node?.GetValue<string>())
                           .Where(value => !string.IsNullOrEmpty(value))
                           .Cast<string>()
                           .ToArray()
                           ?? [];

        SettingsMigrator.TryMigrate(root);

        var twitch = root["twitch"]!.AsObject();
        var botAccountNode = twitch["botAccount"] as JsonObject;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(twitch.ContainsKey("accessToken"), Is.False, "Legacy twitch.accessToken должен исчезнуть, а не переехать вглубь");
            Assert.That(twitch.ContainsKey("refreshToken"), Is.False);
            Assert.That(twitch.ContainsKey("scopes"), Is.False);
            Assert.That(twitch.ContainsKey("storedScopes"), Is.False);

            if (botAccountNode is not null)
            {
                Assert.That(botAccountNode.ContainsKey("accessToken"), Is.False, "Мигратор не должен переливать старый accessToken в botAccount — токен выпущен под недостаточный набор прав");
                Assert.That(botAccountNode.ContainsKey("refreshToken"), Is.False);
                Assert.That(botAccountNode.ContainsKey("scopes"), Is.False, "Целевой набор scopes должен подставиться из дефолтов AppSettings, а не из legacy-JSON");
                Assert.That(botAccountNode.ContainsKey("storedScopes"), Is.False);
                Assert.That(botAccountNode.ContainsKey("accessTokenExpiresAt"), Is.False);
                Assert.That(botAccountNode.ContainsKey("userId"), Is.False);
            }
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = root.Deserialize<AppSettings>(options);
        Assert.That(settings, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings!.Twitch.BotAccount.AccessToken, Is.Empty, "BotAccount.AccessToken — чистый, чтобы запустить новый OAuth-flow");
            Assert.That(settings.Twitch.BotAccount.RefreshToken, Is.Empty);
            Assert.That(settings.Twitch.BotAccount.StoredScopes, Is.Empty, "StoredScopes — это след прошлого согласия, после сброса токенов он не должен держаться");
            Assert.That(settings.Twitch.BotAccount.AccessTokenExpiresAt, Is.Null, "TTL старого токена не должен утаскиваться в новую схему");

            Assert.That(settings.Twitch.BroadcasterAccount.AccessToken, Is.Empty);
            Assert.That(settings.Twitch.BroadcasterAccount.RefreshToken, Is.Empty);
            Assert.That(settings.Twitch.BroadcasterAccount.StoredScopes, Is.Empty);
            Assert.That(settings.Twitch.BroadcasterAccount.AccessTokenExpiresAt, Is.Null);

            foreach (var legacyScope in legacyScopes)
            {
                Assert.That(settings.Twitch.BotAccount.Scopes, Does.Not.Contain(legacyScope),
                    $"Legacy-scope '{legacyScope}' не должен утаскиваться в BotAccount.Scopes — иначе OAuth-flow попросит токен под недостаточный набор прав");

                Assert.That(settings.Twitch.BotAccount.StoredScopes, Does.Not.Contain(legacyScope),
                    $"Legacy-scope '{legacyScope}' не должен оставаться в BotAccount.StoredScopes — это след прошлого согласия, который мешает повторной авторизации");

                Assert.That(settings.Twitch.BroadcasterAccount.Scopes, Does.Not.Contain(legacyScope));
                Assert.That(settings.Twitch.BroadcasterAccount.StoredScopes, Does.Not.Contain(legacyScope));
            }
        }
    }

    [Test]
    public void OldSettingsJson_BotAccountLoginIsCarriedOverFromBotUsername()
    {
        var root = LoadLegacyRoot();
        var legacyBotUsername = (root["twitch"] as JsonObject)?["botUsername"]?.GetValue<string>();

        Assume.That(legacyBotUsername, Is.Not.Null.And.Not.Empty,
            "Тест имеет смысл только если в legacy-файле задан twitch.botUsername");

        SettingsMigrator.TryMigrate(root);

        var botAccount = root["twitch"]!["botAccount"] as JsonObject;
        Assert.That(botAccount, Is.Not.Null, "После миграции должен появиться botAccount, в который переехал botUsername");
        Assert.That(botAccount!["login"]?.GetValue<string>(), Is.EqualTo(legacyBotUsername));
    }

    [Test]
    public void OldSettingsJson_FullCycle_ThroughAtomicFile_RoundTripsCleanly()
    {
        var legacyPath = ResolveLegacySettingsPath();
        Assert.That(File.Exists(legacyPath), Is.True, $"Файл не найден: {legacyPath}");

        var tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempSettings = Path.Combine(tempDir, "settings.json");

        try
        {
            File.Copy(legacyPath, tempSettings);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };

            var initialJson = File.ReadAllText(tempSettings, Encoding.UTF8);
            var root = JsonNode.Parse(initialJson) as JsonObject
                       ?? throw new InvalidOperationException("Корневой элемент не является JSON-объектом");

            var firstChanged = SettingsMigrator.TryMigrate(root, NullLogger.Instance);
            Assert.That(firstChanged, Is.True, "Legacy-файл должен был мигрироваться");

            AtomicFile.Save(tempSettings, root.ToJsonString(options), NullLogger.Instance);

            var diskJson = File.ReadAllText(tempSettings, Encoding.UTF8);

            var rootRoundTrip = JsonNode.Parse(diskJson) as JsonObject
                                ?? throw new InvalidOperationException("Записанный JSON испорчен");

            var secondChanged = SettingsMigrator.TryMigrate(rootRoundTrip, NullLogger.Instance);

            var settings = JsonSerializer.Deserialize<AppSettings>(diskJson, options);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings, Is.Not.Null, "JSON, прошедший через AtomicFile.Save, должен валидно десериализоваться в AppSettings");
                Assert.That(secondChanged, Is.False, "Повторная миграция уже мигрированного и сохранённого через AtomicFile файла не должна ничего менять");

                Assert.That(File.Exists(tempSettings + ".tmp"), Is.False, "После успешного File.Replace .tmp не должен оставаться");
                Assert.That(File.Exists(tempSettings + ".old"), Is.False, ".old удаляется TryDelete после успешного File.Replace");

                Assert.That(settings!.Twitch.BotAccount.AccessToken, Is.Empty);
                Assert.That(settings.Twitch.BotAccount.RefreshToken, Is.Empty);
                Assert.That(settings.Twitch.BotAccount.StoredScopes, Is.Empty);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public void OldSettingsJson_FullPipeline_BothAccountsEndUpWithRequiredScopes()
    {
        var legacyPath = ResolveLegacySettingsPath();
        Assert.That(File.Exists(legacyPath), Is.True, $"Файл не найден: {legacyPath}");

        var tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-migration-scopes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempSettings = Path.Combine(tempDir, "settings.json");

        try
        {
            File.Copy(legacyPath, tempSettings);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };

            var initialJson = File.ReadAllText(tempSettings, Encoding.UTF8);
            var root = JsonNode.Parse(initialJson) as JsonObject
                       ?? throw new InvalidOperationException("Корневой элемент не является JSON-объектом");

            SettingsMigrator.TryMigrate(root, NullLogger.Instance);
            AtomicFile.Save(tempSettings, root.ToJsonString(options), NullLogger.Instance);

            var diskJson = File.ReadAllText(tempSettings, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(diskJson, options);
            Assert.That(settings, Is.Not.Null);

            AppSettingsHydrator.ApplyDefaults(settings!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings!.Twitch.BotAccount.Scopes, Is.EquivalentTo(TwitchScopes.BotRequired),
                    "После полного pipeline BotAccount.Scopes должен содержать актуальный целевой набор для повторной авторизации");

                Assert.That(settings.Twitch.BroadcasterAccount.Scopes, Is.EquivalentTo(TwitchScopes.BroadcasterRequired),
                    "После полного pipeline BroadcasterAccount.Scopes должен содержать актуальный целевой набор");

                Assert.That(settings.Twitch.BotAccount.AccessToken, Is.Empty);
                Assert.That(settings.Twitch.BotAccount.StoredScopes, Is.Empty);
                Assert.That(settings.Twitch.BroadcasterAccount.AccessToken, Is.Empty);
                Assert.That(settings.Twitch.BroadcasterAccount.StoredScopes, Is.Empty);

                Assert.That(TwitchScopes.SetEquals(settings.Twitch.BotAccount.Scopes, settings.Twitch.BotAccount.StoredScopes),
                    Is.False,
                    "Целевые Scopes и пустые StoredScopes должны различаться — иначе после получения токена TwitchOAuthService не зафиксирует scope-mismatch");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public void OldSettingsJson_MigrationIsIdempotent()
    {
        var root = LoadLegacyRoot();

        var firstChanged = SettingsMigrator.TryMigrate(root);
        var snapshot = root.ToJsonString();
        var secondChanged = SettingsMigrator.TryMigrate(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstChanged, Is.True);
            Assert.That(secondChanged, Is.False, "Повторный запуск миграции на уже мигрированном JSON не должен ничего менять");
            Assert.That(root.ToJsonString(), Is.EqualTo(snapshot));
        }
    }
}
