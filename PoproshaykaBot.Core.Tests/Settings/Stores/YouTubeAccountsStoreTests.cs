using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class YouTubeAccountsStoreTests
{
    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("youtube-accounts-store");
        _filePath = Path.Combine(_directory.FullName, "youtube-accounts.json");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    private DirectoryInfo _directory = null!;
    private string _filePath = null!;

    private const string ParseableButInvalid = """
        {
          "clientSecret": "desktop-client-secret",
          "botAccount": {
            "accessToken": "bot-secret-access",
            "refreshToken": "bot-secret-refresh",
            "channelTitle": "TheBot",
            "scopes": "не массив, а строка"
          },
          "broadcasterAccount": {
            "accessToken": "caster-secret-access",
            "refreshToken": "caster-secret-refresh"
          }
        }
        """;

    [Test]
    public void Constructor_FileThatFailsToDeserialize_StartsOnDefaultsAndRedactsClientSecretAndTokens()
    {
        File.WriteAllText(_filePath, ParseableButInvalid);

        var store = Store();
        var backups = Directory.GetFiles(_directory.FullName, "youtube-accounts.invalid-*.json");
        var backup = backups.Length == 1 ? File.ReadAllText(backups[0]) : string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(backup, Is.EqualTo(RedactedOriginal()),
                "Некорректная копия youtube-accounts обязана вымарывать и clientSecret, и accessToken/refreshToken.");

            Assert.That(store.LoadClientSecret(), Is.Empty);
            Assert.That(store.LoadBot().ChannelId, Is.Empty);
            Assert.That(store.LoadBot().Scopes, Is.EquivalentTo(YouTubeScopes.BotRequired));
            Assert.That(store.LoadBroadcaster().Scopes, Is.EquivalentTo(YouTubeScopes.BroadcasterRequired));
            Assert.That(File.ReadAllText(_filePath), Is.EqualTo(ParseableButInvalid));
        }
    }

    [Test]
    public void Save_LeavesNoBackupFileWithPreviousSecrets()
    {
        var store = Store();
        store.SaveAll("old-client-secret", new() { AccessToken = "old-access" }, new());
        store.SaveAll("new-client-secret", new() { AccessToken = "new-access" }, new());

        var leaks = _directory.GetFiles()
            .Where(file => file.FullName != _filePath
                && (File.ReadAllText(file.FullName).Contains("old-client-secret")
                    || File.ReadAllText(file.FullName).Contains("old-access")))
            .Select(file => file.Name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaks, Is.Empty);
            Assert.That(Store().LoadClientSecret(), Is.EqualTo("new-client-secret"));
            Assert.That(Store().LoadBot().AccessToken, Is.EqualTo("new-access"));
        }
    }

    [Test]
    public void TryClearAccessToken_WithMatchingToken_ClearsOnlyAccessTokenAndExpiry()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "yt-a1";
            account.RefreshToken = "yt-r1";
            account.ChannelId = "UC123";
            account.AccessTokenExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50);
        });

        var cleared = store.TryClearAccessToken(PlatformAuthRole.Bot, "yt-a1");
        var bot = Store().LoadBot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared, Is.True);
            Assert.That(bot.AccessToken, Is.Empty);
            Assert.That(bot.AccessTokenExpiresAt, Is.Null);
            Assert.That(bot.RefreshToken, Is.EqualTo("yt-r1"));
            Assert.That(bot.ChannelId, Is.EqualTo("UC123"));
        }
    }

    [Test]
    public void TryReplaceAccounts_TargetsNamedContextsWithoutTouchingDefault()
    {
        var store = Store();
        store.SaveAll("orig-secret",
            new() { ChannelId = "UC_DEF_BOT", RefreshToken = "def-bot-rt" },
            new() { ChannelId = "UC_DEF_STR", RefreshToken = "def-str-rt" });
        store.UpsertContext(PlatformAuthRole.Bot, "yt-bot-2", "Бот 2",
            new() { ChannelId = "UC_BOT_2", RefreshToken = "bot2-rt", StoredScopes = [YouTubeScopes.ForceSsl] });
        store.UpsertContext(PlatformAuthRole.Broadcaster, "yt-str-2", "Стример 2",
            new() { ChannelId = "UC_STR_2", RefreshToken = "str2-rt", StoredScopes = [YouTubeScopes.ReadOnly] });

        var expectedBot = store.Load(PlatformAuthRole.Bot, "yt-bot-2");
        var expectedBroadcaster = store.Load(PlatformAuthRole.Broadcaster, "yt-str-2");
        var updatedBot = new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT_2",
            RefreshToken = "bot2-rt-new",
            StoredScopes = [YouTubeScopes.ForceSsl],
        };
        var updatedBroadcaster = new YouTubeAccountSettings
        {
            ChannelId = "UC_STR_2",
            RefreshToken = "str2-rt-new",
            StoredScopes = [YouTubeScopes.ForceSsl],
        };

        var replaced = store.TryReplaceAccounts(
            "yt-bot-2",
            "yt-str-2",
            ("orig-secret", expectedBot, expectedBroadcaster),
            ("new-secret", updatedBot, updatedBroadcaster));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(replaced.Matched, Is.True);
            Assert.That(replaced.Written, Is.True);
            Assert.That(store.LoadClientSecret(), Is.EqualTo("new-secret"));
            Assert.That(store.Load(PlatformAuthRole.Bot, "yt-bot-2").RefreshToken, Is.EqualTo("bot2-rt-new"));
            Assert.That(store.Load(PlatformAuthRole.Broadcaster, "yt-str-2").RefreshToken, Is.EqualTo("str2-rt-new"));
            Assert.That(store.LoadBot().RefreshToken, Is.EqualTo("def-bot-rt"));
            Assert.That(store.LoadBroadcaster().RefreshToken, Is.EqualTo("def-str-rt"));
        }

        var rolledBack = store.TryReplaceAccounts(
            "yt-bot-2",
            "yt-str-2",
            ("new-secret", updatedBot, updatedBroadcaster),
            ("orig-secret", expectedBot, expectedBroadcaster));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rolledBack.Matched, Is.True);
            Assert.That(rolledBack.Written, Is.True);
            Assert.That(store.LoadClientSecret(), Is.EqualTo("orig-secret"));
            Assert.That(store.Load(PlatformAuthRole.Bot, "yt-bot-2").RefreshToken, Is.EqualTo("bot2-rt"));
            Assert.That(store.Load(PlatformAuthRole.Broadcaster, "yt-str-2").RefreshToken, Is.EqualTo("str2-rt"));
        }
    }

    [Test]
    public void TryReplaceClientSecretAndAccount_RollBackIndependentlyWhenOtherSlotChangedExternally()
    {
        var store = Store();
        var origBot = new YouTubeAccountSettings { ChannelId = "UC_BOT", RefreshToken = "orig-bot-rt", StoredScopes = [YouTubeScopes.ForceSsl] };
        var origBroadcaster = new YouTubeAccountSettings { ChannelId = "UC_STR", RefreshToken = "orig-str-rt", StoredScopes = [YouTubeScopes.ForceSsl] };
        store.SaveAll("orig-secret", origBot, origBroadcaster);

        var wizardBot = new YouTubeAccountSettings { ChannelId = "UC_BOT", RefreshToken = "wizard-bot-rt", StoredScopes = [YouTubeScopes.ForceSsl] };
        var wizardBroadcaster = new YouTubeAccountSettings { ChannelId = "UC_STR", RefreshToken = "wizard-str-rt", StoredScopes = [YouTubeScopes.ForceSsl] };

        Assert.That(store.TryReplaceClientSecret("orig-secret", "wizard-secret").Written, Is.True);
        Assert.That(store.TryReplaceAccount(PlatformAuthRole.Bot, AuthContextIds.Default, origBot, wizardBot).Written, Is.True);
        Assert.That(store.TryReplaceAccount(PlatformAuthRole.Broadcaster, AuthContextIds.Default, origBroadcaster, wizardBroadcaster).Written, Is.True);

        store.Mutate(PlatformAuthRole.Broadcaster, account => account.RefreshToken = "external-str-rt");

        var rollbackSecret = store.TryReplaceClientSecret("wizard-secret", "orig-secret");
        var rollbackBot = store.TryReplaceAccount(PlatformAuthRole.Bot, AuthContextIds.Default, wizardBot, origBot);
        var rollbackBroadcaster = store.TryReplaceAccount(PlatformAuthRole.Broadcaster, AuthContextIds.Default, wizardBroadcaster, origBroadcaster);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rollbackSecret, Is.EqualTo((true, true)));
            Assert.That(rollbackBot, Is.EqualTo((true, true)));
            Assert.That(rollbackBroadcaster, Is.EqualTo((false, false)));
            Assert.That(store.LoadClientSecret(), Is.EqualTo("orig-secret"));
            Assert.That(store.LoadBot().RefreshToken, Is.EqualTo("orig-bot-rt"));
            Assert.That(store.LoadBroadcaster().RefreshToken, Is.EqualTo("external-str-rt"));
        }
    }

    private static string RedactedOriginal()
    {
        var expected = JsonNode.Parse(ParseableButInvalid)!;
        expected["clientSecret"] = string.Empty;

        foreach (var account in (string[])["botAccount", "broadcasterAccount"])
        {
            expected[account]!["accessToken"] = string.Empty;
            expected[account]!["refreshToken"] = string.Empty;
        }

        return expected.ToJsonString(JsonStoreOptions.Default);
    }

    private YouTubeAccountsStore Store()
    {
        return new(NullLogger<YouTubeAccountsStore>.Instance, _filePath);
    }
}
