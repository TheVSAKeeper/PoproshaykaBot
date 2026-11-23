using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class AccountsStoreTests
{
    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("accounts-store");
        _filePath = Path.Combine(_directory.FullName, "accounts.json");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    private DirectoryInfo _directory = null!;
    private string _filePath = null!;

    private const int RacerCount = 32;

    [Test]
    public void Verifying_authorization_reads_disk_and_detects_an_older_write_for_the_same_account()
    {
        var store = Store();
        var role = PlatformAuthRole.Bot;
        store.UpsertContext(role, "named", "bot", new()
        {
            UserId = "42", AccessToken = "first",
        });
        var older = File.ReadAllText(_filePath);
        store.UpsertContext(role, "named", "bot", new()
        {
            UserId = "42", AccessToken = "second",
        });
        Assert.That(store.VerifyPersistedAuthorization(role, "named", "42"), Is.True);

        File.WriteAllText(_filePath, older);

        Assert.That(store.VerifyPersistedAuthorization(role, "named", "42"), Is.False);
        Assert.That(store.Load(role, "named").AccessToken, Is.EqualTo("second"));
    }

    [TestCase("default")]
    [TestCase("named")]
    public void Reauthorizing_in_system_browser_keeps_the_profile_only_for_the_same_account(string contextId)
    {
        var store = Store();
        var profile = Guid.NewGuid().ToString("N");
        var account = new TwitchAccountSettings { UserId = "42", Login = "first", BrowserProfileName = profile };
        store.UpsertContext(PlatformAuthRole.Bot, contextId, account.Login, account);

        var reopened = Store();
        reopened.UpsertContext(PlatformAuthRole.Bot, contextId, "first", new() { UserId = "42", Login = "first" });
        Assert.That(Store().Load(PlatformAuthRole.Bot, contextId).BrowserProfileName, Is.EqualTo(profile));

        reopened.UpsertContext(PlatformAuthRole.Bot, contextId, "second", new() { UserId = "43", Login = "second" });
        Assert.That(Store().Load(PlatformAuthRole.Bot, contextId).BrowserProfileName, Is.Null);
    }

    private const string ParseableButInvalid = """
        {
          "botAccount": {
            "accessToken": "bot-secret-access",
            "refreshToken": "bot-secret-refresh",
            "login": "thebot",
            "scopes": "не массив, а строка"
          },
          "broadcasterAccount": {
            "accessToken": "caster-secret-access",
            "refreshToken": "caster-secret-refresh"
          }
        }
        """;

    [Test]
    public void TryClearAccessToken_WithTheTokenThatGotThe401_ClearsItAndKeepsTheRestOfTheAccount()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "v1";
            account.RefreshToken = "r1";
            account.Login = "thebot";
            account.AccessTokenExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50);
        });

        var cleared = store.TryClearAccessToken(PlatformAuthRole.Bot, "v1");
        var account = store.LoadBot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared, Is.True);
            Assert.That(account.AccessToken, Is.Empty);
            Assert.That(account.AccessTokenExpiresAt, Is.Null, "Протухший TTL пережил бы очистку и удержал бы кэш от повторного получения токена.");
            Assert.That(account.RefreshToken, Is.EqualTo("r1"), "401 по access-токену не повод выбрасывать refresh – иначе пользователя гонят на повторную авторизацию.");
            Assert.That(account.Login, Is.EqualTo("thebot"));
            Assert.That(Store().LoadBot().AccessToken, Is.Empty, "Очистка обязана дойти до диска, а не остаться в памяти процесса.");
        }
    }

    [Test]
    public void TryClearAccessToken_WithATokenTheRefreshAlreadyReplaced_LeavesTheFreshOneAlone()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "v1");

        store.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "v2";
            account.AccessTokenExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50);
        });

        var cleared = store.TryClearAccessToken(PlatformAuthRole.Bot, "v1");
        var account = store.LoadBot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared, Is.False);
            Assert.That(account.AccessToken, Is.EqualTo("v2"), "401 по устаревшему токену не должен стирать тот, который уехал вперёд после refresh.");
            Assert.That(account.AccessTokenExpiresAt, Is.EqualTo(DateTimeOffset.UnixEpoch.AddYears(50)));
            Assert.That(Store().LoadBot().AccessToken, Is.EqualTo("v2"));
        }
    }

    [TestCase(PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster)]
    [TestCase(PlatformAuthRole.Broadcaster, PlatformAuthRole.Bot)]
    public void TryClearAccessToken_ForOneRole_LeavesTheOtherRoleUntouched(PlatformAuthRole cleared, PlatformAuthRole untouched)
    {
        var store = Store();
        store.Mutate(cleared, account => account.AccessToken = "token-" + cleared);
        store.Mutate(untouched, account => account.AccessToken = "token-" + untouched);

        store.TryClearAccessToken(cleared, "token-" + cleared);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Load(cleared).AccessToken, Is.Empty);
            Assert.That(store.Load(untouched).AccessToken, Is.EqualTo("token-" + untouched));
            Assert.That(Store().Load(untouched).AccessToken, Is.EqualTo("token-" + untouched),
                "Обе роли живут в одном файле – запись по одной не имеет права уронить вторую.");
        }
    }

    [Test]
    public void TryClearAccessToken_CalledByEveryRacingRequestAtOnce_SucceedsExactlyOnce()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "v1");

        var winners = 0;

        using var linedUp = new CountdownEvent(RacerCount);
        using var start = new ManualResetEventSlim(false);

        var racers = Enumerable.Range(0, RacerCount)
            .Select(_ => new Thread(() =>
            {
                linedUp.Signal();
                start.Wait();

                if (store.TryClearAccessToken(PlatformAuthRole.Bot, "v1"))
                {
                    Interlocked.Increment(ref winners);
                }
            }))
            .ToArray();

        foreach (var racer in racers)
        {
            racer.Start();
        }

        linedUp.Wait();
        start.Set();

        foreach (var racer in racers)
        {
            racer.Join();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(winners, Is.EqualTo(1), "Сравнение с ожидаемым токеном и очистка обязаны идти под одним замком, иначе 401-обработчики выигрывают наперегонки.");
            Assert.That(store.LoadBot().AccessToken, Is.Empty);
        }
    }

    [Test]
    public void TryClearAccessToken_WithATokenTheRefreshAlreadyReplaced_WritesNothingAtAll()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "v2");

        Directory.CreateDirectory(_filePath + ".old");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.TryClearAccessToken(PlatformAuthRole.Bot, "v1"), Is.False);

            Assert.That(store.LoadBot().AccessToken, Is.EqualTo("v2"),
                "Пропущенная очистка не должна ходить на диск: запись заблокирована, и любое обращение к ней здесь упало бы. 401 по устаревшему токену прилетает пачками.");
        }
    }

    [Test]
    public void Mutate_WhenTheWriteFails_KeepsTheCacheOnTheVersionThatReachedDisk()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "v1");

        Directory.CreateDirectory(_filePath + ".old");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "v2"),
                Throws.InstanceOf<IOException>());

            Assert.That(store.LoadBot().AccessToken, Is.EqualTo("v1"),
                "Токен, не дошедший до диска, не должен оседать в памяти: следующая удачная запись затрёт им целый файл.");
        }
    }

    [Test]
    public void Constructor_FileThatFailsToDeserialize_StartsOnDefaultsAndSetsItAsideWithoutTokens()
    {
        File.WriteAllText(_filePath, ParseableButInvalid);

        var store = Store();
        var backups = Directory.GetFiles(_directory.FullName, "accounts.invalid-*.json");
        var backup = backups.Length == 1 ? File.ReadAllText(backups[0]) : string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(backup, Is.EqualTo(RedactedOriginal()),
                "Бэкап – это оригинал с вычеркнутыми токенами: всё остальное обязано дойти до разбора целиком.");

            Assert.That(store.LoadBot().Login, Is.Empty, "Нечитаемый файл поднимает приложение на дефолтах, а не роняет его.");
            Assert.That(store.LoadBot().Scopes, Is.EquivalentTo(TwitchScopes.BotRequired));
            Assert.That(store.LoadBroadcaster().Scopes, Is.EquivalentTo(TwitchScopes.BroadcasterRequired));
            Assert.That(File.ReadAllText(_filePath), Is.EqualTo(ParseableButInvalid), "Оригинал остаётся на месте до первой записи стора.");
        }
    }

    [Test]
    public void Constructor_FileThatIsNotJsonAtAll_SetsAsideAnEmptyBackup()
    {
        File.WriteAllText(_filePath, "{ это вообще не json");

        Store();

        var backups = Directory.GetFiles(_directory.FullName, "accounts.invalid-*.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(backups[0]), Is.EqualTo("{}"),
                "Редактор не разбирает битый JSON и отдаёт пустой объект: содержимое бэкапа теряется целиком.");
        }
    }

    [Test]
    public void Запись_аккаунтов_не_оставляет_рядом_откатную_копию_с_прежними_токенами()
    {
        var store = Store();
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "old-secret-access");
        store.Mutate(PlatformAuthRole.Bot, account => account.AccessToken = "new-secret-access");

        var leaks = _directory.GetFiles()
            .Where(file => file.FullName != _filePath && File.ReadAllText(file.FullName).Contains("secret-access"))
            .Select(file => file.Name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaks, Is.Empty,
                "Откатная копия нужна только на время подмены; пережив её, она держит токены открытым текстом – а после переноса данных и весь сеанс.");

            Assert.That(Store().LoadBot().AccessToken, Is.EqualTo("new-secret-access"));
        }
    }

    [Test]
    public void Переавторизация_одной_роли_через_встроенный_браузер_обновляет_профиль_соседней_роли()
    {
        var store = Store();
        var oldProfile = Guid.NewGuid().ToString("N");
        var newProfile = Guid.NewGuid().ToString("N");

        store.UpsertContexts([
            (PlatformAuthRole.Broadcaster, "tw-broadcaster-42", "alpha", new TwitchAccountSettings
            {
                UserId = "42", Login = "alpha", AccessToken = "a1", RefreshToken = "r1", BrowserProfileName = oldProfile,
            }),
            (PlatformAuthRole.Bot, "tw-bot-42", "alpha", new TwitchAccountSettings
            {
                UserId = "42", Login = "alpha", AccessToken = "a1", RefreshToken = "r1", BrowserProfileName = oldProfile,
            }),
        ]);

        store.UpsertContext(PlatformAuthRole.Bot, "tw-bot-42", "alpha", new TwitchAccountSettings
        {
            UserId = "42", Login = "alpha", AccessToken = "a2", RefreshToken = "r2", BrowserProfileName = newProfile,
        });

        store.UpsertContext(PlatformAuthRole.Bot, "tw-bot-42", "alpha", new TwitchAccountSettings
        {
            UserId = "42", Login = "alpha", AccessToken = "a3", RefreshToken = "r3", BrowserProfileName = null,
        });

        var account = store.ListAccounts().Single(entry => entry.Key.AccountId == "42");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Load(PlatformAuthRole.Broadcaster, "tw-broadcaster-42").BrowserProfileName, Is.EqualTo(newProfile));
            Assert.That(store.Load(PlatformAuthRole.Bot, "tw-bot-42").BrowserProfileName, Is.EqualTo(newProfile));
            Assert.That(account.Authorizations.Select(grant => grant.ChatBrowserProfile?.ProfileName), Is.All.EqualTo(newProfile));
        }
    }

    private static string RedactedOriginal()
    {
        var expected = JsonNode.Parse(ParseableButInvalid)!;

        foreach (var account in (string[])["botAccount", "broadcasterAccount"])
        {
            expected[account]!["accessToken"] = string.Empty;
            expected[account]!["refreshToken"] = string.Empty;
        }

        return expected.ToJsonString(JsonStoreOptions.Default);
    }

    private AccountsStore Store()
    {
        return new(NullLogger<AccountsStore>.Instance, _filePath);
    }
}
