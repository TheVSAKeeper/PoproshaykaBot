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
        store.Mutate(YouTubeOAuthRole.Bot, account =>
        {
            account.AccessToken = "yt-a1";
            account.RefreshToken = "yt-r1";
            account.ChannelId = "UC123";
            account.AccessTokenExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50);
        });

        var cleared = store.TryClearAccessToken(YouTubeOAuthRole.Bot, "yt-a1");
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
