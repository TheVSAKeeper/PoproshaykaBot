using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Tests.Twitch.Helix;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.Users.Avatars;
using System.Net;
using System.Net.Http.Headers;

namespace PoproshaykaBot.Core.Tests.Users.Avatars;

[TestFixture]
public sealed class UserAvatarCacheTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private string _directory = null!;
    private TestTimeProvider _time = null!;
    private RecordingLogger<UserAvatarCache> _logger = null!;
    private ITwitchHelixClient _helix = null!;
    private StubHttpMessageHandler _cdn = null!;
    private List<int> _batches = null!;
    private Dictionary<string, string> _avatars = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-avatars-" + Guid.NewGuid().ToString("N"));
        _time = new() { UtcNow = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero) };
        _logger = new();
        _cdn = new() { Responder = _ => Image(Png) };
        _batches = [];
        _avatars = new(StringComparer.Ordinal);
        _helix = Substitute.For<ITwitchHelixClient>();

        _helix
            .GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IEnumerable<string>>().ToArray();
                _batches.Add(ids.Length);

                return Task.FromResult<IReadOnlyList<UserInfo>>(ids
                    .Where(_avatars.ContainsKey)
                    .Select(id => new UserInfo(id, "u" + id, "U" + id, "", "", null, _avatars[id], null, DateTime.UnixEpoch))
                    .ToArray());
            });
    }

    [TearDown]
    public void TearDown()
    {
        _cdn.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Четыреста_двадцать_два_пользователя_разрешаются_пятью_запросами_и_повторно_не_спрашиваются()
    {
        var ids = Known(422);
        var cache = Create();

        await cache.ResolveAsync(ids);
        await cache.ResolveAsync(ids);
        await Create().ResolveAsync(ids);

        Assert.That(_batches, Is.EqualTo(new[] { 100, 100, 100, 100, 22 }),
            "прогретый адрес не спрашивается ни в том же процессе, ни после перезапуска");
    }

    [Test]
    public async Task Картинки_качаются_только_для_запрошенных_и_в_уменьшенном_размере()
    {
        var ids = Known(422);
        var cache = Create();

        await cache.ResolveAsync(ids);
        var paths = await cache.GetPathsAsync(ids.Take(3).ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Keys, Is.EquivalentTo(ids.Take(3)));
            Assert.That(paths.Values.Select(path => Path.GetFileName(path)), Is.EquivalentTo(ids.Take(3).Select(id => id + ".img")),
                "файл называется по userId, а не по нику");
            Assert.That(_cdn.Requests, Has.Count.EqualTo(3));
            Assert.That(_cdn.Requests[0].RequestUri!.AbsoluteUri, Does.EndWith("-profile_image-150x150.png"));
        }
    }

    [Test]
    public async Task Прогретый_кеш_после_перезапуска_отдаёт_картинки_без_сети()
    {
        var ids = Known(5);
        var warm = Create();
        await warm.ResolveAsync(ids);
        await warm.GetPathsAsync(ids);
        _batches.Clear();
        _cdn.Requests.Clear();

        var restarted = Create();
        var cached = restarted.GetCachedPaths(ids);
        await restarted.ResolveAsync(ids);
        var loaded = await restarted.GetPathsAsync(ids);
        var url = await restarted.GetUrlAsync(ids[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cached.Keys, Is.EquivalentTo(ids));
            Assert.That(loaded, Is.EqualTo(cached));
            Assert.That(url, Is.EqualTo(_avatars[ids[0]]), "оверлей берёт адрес из того же кеша");
            Assert.That(_batches, Is.Empty);
            Assert.That(_cdn.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Уменьшенного_размера_нет_берётся_исходный_адрес()
    {
        var ids = Known(1);
        _cdn.Responder = request => request.RequestUri!.AbsoluteUri.Contains("150x150")
            ? new(HttpStatusCode.NotFound)
            : Image(Png);

        var cache = Create();
        await cache.ResolveAsync(ids);
        var paths = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Keys, Is.EquivalentTo(ids));
            Assert.That(_cdn.Requests.Select(request => request.RequestUri!.AbsoluteUri),
                Is.EqualTo(new[] { _avatars[ids[0]].Replace("300x300", "150x150"), _avatars[ids[0]] }));
        }
    }

    [Test]
    public async Task Неизвестного_Twitch_пользователя_не_спрашивает_до_срока_промаха()
    {
        var cache = Create();

        await cache.ResolveAsync(["999"]);
        var overlay = await cache.GetUrlAsync("999");

        _time.UtcNow += UserAvatarCacheOptions.DefaultMissLifetime - TimeSpan.FromMinutes(1);
        await Create().ResolveAsync(["999"]);

        _time.UtcNow += TimeSpan.FromMinutes(1);
        await cache.ResolveAsync(["999"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overlay, Is.Null);
            Assert.That(_batches, Is.EqualTo(new[] { 1, 1 }), "промах переживает перезапуск и истекает через свой срок");
        }
    }

    [TestCase("not-image", TestName = "Не_картинка_в_кеш_не_попадает_и_не_качается_до_срока_промаха")]
    [TestCase("empty", TestName = "Пустая_картинка_в_кеш_не_попадает_и_не_качается_до_срока_промаха")]
    [TestCase("too-large", TestName = "Слишком_большая_картинка_в_кеш_не_попадает_и_не_качается_до_срока_промаха")]
    [TestCase("not-saved", TestName = "Незаписанная_на_диск_картинка_не_качается_заново_до_срока_промаха")]
    public async Task Сломанная_картинка_не_попадает_в_кеш(string kind)
    {
        var ids = Known(1);
        var blocker = Path.Combine(_directory, ids[0] + UserAvatarCache.ImageExtension);
        _cdn.Responder = _ => kind switch
        {
            "not-image" => Image("<html>ошибка</html>"u8.ToArray()),
            "empty" => Image([]),
            "not-saved" => Image(Png),
            _ => Image([.. Png, .. new byte[2048]]),
        };

        if (kind == "not-saved")
        {
            Directory.CreateDirectory(blocker);
        }

        var cache = Create(maxImageBytes: 1024);
        await cache.ResolveAsync(ids);

        var first = await cache.GetPathsAsync(ids);
        var requestsAfterFirst = _cdn.Requests.Count;
        var second = await Create(maxImageBytes: 1024).GetPathsAsync(ids);
        var filesBeforeRetry = Directory.EnumerateFiles(_directory, "*.img").ToList();

        if (kind == "not-saved")
        {
            Directory.Delete(blocker, recursive: true);
        }

        _time.UtcNow += UserAvatarCacheOptions.DefaultMissLifetime;
        _cdn.Responder = _ => Image(Png);
        var retried = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty, "отказ картинки переживает перезапуск");
            Assert.That(filesBeforeRetry, Is.Empty);
            Assert.That(_cdn.Requests, Has.Count.EqualTo(requestsAfterFirst + 1),
                "до срока промаха повторной загрузки нет, после него – одна");
            Assert.That(retried.Keys, Is.EquivalentTo(ids));
        }
    }

    [Test]
    public async Task Испорченный_файл_убирается_и_не_качается_заново_до_срока_промаха()
    {
        var ids = Known(1);
        var cache = Create();
        await cache.ResolveAsync(ids);
        var path = (await cache.GetPathsAsync(ids))[ids[0]];

        cache.Invalidate(ids[0]);
        var again = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(path), Is.False);
            Assert.That(again, Is.Empty);
            Assert.That(cache.GetCachedPaths(ids), Is.Empty);
            Assert.That(_cdn.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Адрес_пересчитывается_после_срока_и_смена_аватарки_перекачивает_картинку()
    {
        var ids = Known(1);
        var cache = Create();
        await cache.ResolveAsync(ids);
        await cache.GetPathsAsync(ids);

        _time.UtcNow += UserAvatarCacheOptions.DefaultUrlLifetime - TimeSpan.FromMinutes(1);
        var early = await cache.ResolveAsync(ids);

        _avatars[ids[0]] = "https://static-cdn.jtvnw.net/jtv_user_pictures/new-profile_image-300x300.png";
        _time.UtcNow += TimeSpan.FromMinutes(1);
        var changed = await cache.ResolveAsync(ids);
        var staleHidden = cache.GetCachedPaths(ids);
        var reloaded = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(early, Is.Empty);
            Assert.That(_batches, Is.EqualTo(new[] { 1, 1 }), "до срока адрес не пересчитывается");
            Assert.That(changed, Is.EquivalentTo(ids));
            Assert.That(staleHidden, Is.Empty, "картинка прежнего адреса текущей не считается");
            Assert.That(reloaded.Keys, Is.EquivalentTo(ids));
            Assert.That(_cdn.Requests[^1].RequestUri!.AbsoluteUri, Does.Contain("/new-profile_image-150x150"));
            Assert.That(await cache.GetUrlAsync(ids[0]), Is.EqualTo(_avatars[ids[0]]));
        }
    }

    [Test]
    public async Task Потолок_размера_вытесняет_самые_давние_картинки()
    {
        var ids = Known(3);
        var cache = Create(maxTotalBytes: Png.Length * 2);
        await cache.ResolveAsync(ids);

        await cache.GetPathsAsync([ids[0]]);
        _time.UtcNow += TimeSpan.FromHours(2);
        await cache.GetPathsAsync([ids[1]]);
        _time.UtcNow += TimeSpan.FromHours(2);
        var last = await cache.GetPathsAsync([ids[2]]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(last.Keys, Is.EquivalentTo(new[] { ids[2] }));
            Assert.That(Directory.EnumerateFiles(_directory, "*.img").Select(path => Path.GetFileName(path)),
                Is.EquivalentTo(new[] { ids[1] + ".img", ids[2] + ".img" }));
        }
    }

    [Test]
    public async Task Смена_адреса_после_уборки_файла_по_потолку_отмечается_а_первое_разрешение_нет()
    {
        var known = Known(3);
        string[] ids = [known[0], known[1]];
        var newcomer = known[2];
        var cache = Create(maxTotalBytes: Png.Length);
        var first = await cache.ResolveAsync(ids);

        await cache.GetPathsAsync([ids[0]]);
        _time.UtcNow += TimeSpan.FromHours(2);
        await cache.GetPathsAsync([ids[1]]);
        var swept = !File.Exists(Path.Combine(_directory, ids[0] + ".img"));

        _avatars[ids[0]] = "https://static-cdn.jtvnw.net/jtv_user_pictures/new-profile_image-300x300.png";
        _time.UtcNow += UserAvatarCacheOptions.DefaultUrlLifetime;
        var changed = await cache.ResolveAsync([.. ids, newcomer]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty, "первое разрешение записи сменой не считается");
            Assert.That(swept, Is.True, "уборка по потолку убрала файл первого");
            Assert.That(changed, Is.EquivalentTo(new[] { ids[0] }), "показанная до уборки картинка устарела");
        }
    }

    [Test]
    public async Task Удалённый_на_Twitch_пользователь_числится_без_адреса()
    {
        var ids = Known(2);
        var cache = Create();
        await cache.ResolveAsync(ids);
        await cache.GetPathsAsync(ids);

        _avatars.Remove(ids[0]);
        _time.UtcNow += UserAvatarCacheOptions.DefaultUrlLifetime;
        var changed = await cache.ResolveAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.EquivalentTo(new[] { ids[0] }));
            Assert.That(cache.GetUnavailable([.. ids, "404"]), Is.EquivalentTo(new[] { ids[0] }),
                "без записи о пользователе кеш отсутствие адреса не утверждает");
        }
    }

    [Test]
    public async Task Потолок_числа_записей_вытесняет_самых_давних_вместе_с_картинкой()
    {
        var ids = Known(3);
        var cache = Create(maxEntries: 1);

        await cache.ResolveAsync([ids[0]]);
        await cache.GetPathsAsync([ids[0]]);
        _time.UtcNow += TimeSpan.FromHours(2);
        await cache.ResolveAsync([ids[1]]);
        _time.UtcNow += TimeSpan.FromHours(2);
        await cache.ResolveAsync([ids[2]]);
        var filesAfterTrim = Directory.EnumerateFiles(_directory, "*.img").ToList();
        await cache.ResolveAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filesAfterTrim, Is.Empty, "картинка уходит вместе с вытесненной записью");
            Assert.That(_batches, Is.EqualTo(new[] { 1, 1, 1, 1 }), "заново спрашивается только вытесненный");
        }
    }

    [Test]
    public async Task Набор_страницы_больше_потолка_записей_не_вытесняет_сам_себя()
    {
        var ids = Known(3);
        var cache = Create(maxEntries: 2);

        await cache.ResolveAsync(ids);
        await cache.GetPathsAsync(ids);
        await cache.ResolveAsync(ids);
        var again = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_batches, Is.EqualTo(new[] { 3 }), "второй проход того же набора в Helix не ходит");
            Assert.That(_cdn.Requests, Has.Count.EqualTo(3), "картинки набора не перекачиваются");
            Assert.That(again.Keys, Is.EquivalentTo(ids));
            Assert.That(Directory.EnumerateFiles(_directory, "*.img").Count(), Is.EqualTo(3));
        }
    }

    [Test]
    public async Task Потолок_записей_вне_набора_страницы_вытесняет_записи_оверлея()
    {
        var page = Known(3);
        var overlay = Enumerable.Range(2000, 3).Select(index => index.ToString()).ToArray();

        foreach (var id in overlay)
        {
            _avatars[id] = $"https://static-cdn.jtvnw.net/jtv_user_pictures/{id}-profile_image-300x300.png";
        }

        var cache = Create(maxEntries: 2);
        await cache.ResolveAsync(page);

        foreach (var id in overlay)
        {
            _time.UtcNow += TimeSpan.FromHours(2);
            await cache.GetUrlAsync(id);
        }

        await cache.ResolveAsync(page);
        await cache.GetUrlAsync(overlay[2]);
        await cache.GetUrlAsync(overlay[0]);

        Assert.That(_batches, Is.EqualTo(new[] { 3, 1, 1, 1, 1 }),
            "набор страницы не вытесняется, из записей оверлея уходит самая давняя");
    }

    [TestCase(false, TestName = "Новый_адрес_из_прохода_страницы_качается_сразу_после_отказа_прежней_картинки")]
    [TestCase(true, TestName = "Новый_адрес_из_запроса_оверлея_качается_сразу_после_отказа_прежней_картинки")]
    public async Task Новый_адрес_качается_сразу_после_отказа_прежней_картинки(bool viaOverlay)
    {
        var ids = Known(1);
        var cache = Create();
        await cache.ResolveAsync(ids);

        _time.UtcNow += UserAvatarCacheOptions.DefaultUrlLifetime - TimeSpan.FromMinutes(10);
        _cdn.Responder = _ => Image("<html>ошибка</html>"u8.ToArray());
        var rejected = await cache.GetPathsAsync(ids);

        _avatars[ids[0]] = "https://static-cdn.jtvnw.net/jtv_user_pictures/new-profile_image-300x300.png";
        _cdn.Responder = _ => Image(Png);
        _time.UtcNow += TimeSpan.FromMinutes(10);

        if (viaOverlay)
        {
            await cache.GetUrlAsync(ids[0]);
        }
        else
        {
            await cache.ResolveAsync(ids);
        }

        var paths = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected, Is.Empty);
            Assert.That(paths.Keys, Is.EquivalentTo(ids), "отказ прежнего адреса новый не держит");
            Assert.That(_cdn.Requests[^1].RequestUri!.AbsoluteUri, Does.Contain("/new-profile_image-150x150"));
        }
    }

    [Test]
    public async Task Смена_адреса_узнанная_оверлеем_доезжает_до_следующего_прохода_страницы()
    {
        var ids = Known(1);
        var cache = Create();
        await cache.ResolveAsync(ids);
        await cache.GetPathsAsync(ids);

        _avatars[ids[0]] = "https://static-cdn.jtvnw.net/jtv_user_pictures/new-profile_image-300x300.png";
        _time.UtcNow += UserAvatarCacheOptions.DefaultUrlLifetime;
        var url = await cache.GetUrlAsync(ids[0]);

        var changed = await cache.ResolveAsync(ids);
        var repeated = await cache.ResolveAsync(ids);
        var reloaded = await cache.GetPathsAsync(ids);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(url, Is.EqualTo(_avatars[ids[0]]));
            Assert.That(changed, Is.EquivalentTo(ids), "проход страницы узнаёт о смене, хотя адрес уже свежий");
            Assert.That(repeated, Is.Empty, "о смене сообщается один раз");
            Assert.That(_batches, Is.EqualTo(new[] { 1, 1 }), "свежий адрес заново не спрашивается");
            Assert.That(reloaded.Keys, Is.EquivalentTo(ids));
            Assert.That(_cdn.Requests[^1].RequestUri!.AbsoluteUri, Does.Contain("/new-profile_image-150x150"));
        }
    }

    [Test]
    public async Task Без_токена_пустая_выдача_и_одно_предупреждение()
    {
        _helix
            .GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TwitchAuthorizationMissingException("нет токена"));

        var ids = Enumerable.Range(1, 250).Select(index => index.ToString()).ToArray();
        var cache = Create();

        var changed = await cache.ResolveAsync(ids);
        await cache.ResolveAsync(ids);
        var paths = await cache.GetPathsAsync(ids);
        var url = await cache.GetUrlAsync("1");
        var warnings = _logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.Empty);
            Assert.That(paths, Is.Empty);
            Assert.That(url, Is.Null);
            Assert.That(_cdn.Requests, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain("нет авторизации Twitch"));
            Assert.That(warnings[0].Message, Does.Not.Contain("нет токена"), "текст исключения в журнал не попадает");

            await _helix.Received(3).GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task Режим_только_из_кеша_в_сеть_не_ходит()
    {
        var ids = Known(2);
        var cache = Create(cacheOnly: true);

        var changed = await cache.ResolveAsync(ids);
        var paths = await cache.GetPathsAsync(ids);
        var url = await cache.GetUrlAsync(ids[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.Empty);
            Assert.That(paths, Is.Empty);
            Assert.That(url, Is.Null);
            Assert.That(_batches, Is.Empty);
            Assert.That(_cdn.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Битый_индекс_не_роняет_кеш()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, UserAvatarCache.IndexFileName), "{ не json");
        var ids = Known(1);

        var cache = Create();
        await cache.ResolveAsync(ids);
        var paths = await cache.GetPathsAsync(ids);

        Assert.That(paths.Keys, Is.EquivalentTo(ids));
    }

    [Test]
    public async Task Параллельные_запросы_оверлея_одного_пользователя_идут_в_Helix_один_раз()
    {
        var ids = Known(1);
        var gate = new TaskCompletionSource();

        _helix
            .GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task;

                IReadOnlyList<UserInfo> users = [new UserInfo(ids[0], "u", "U", "", "", null, _avatars[ids[0]], null, DateTime.UnixEpoch)];

                return users;
            });

        var cache = Create();
        var first = cache.GetUrlAsync(ids[0]);
        var second = cache.GetUrlAsync(ids[0]);
        gate.SetResult();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await first, Is.EqualTo(_avatars[ids[0]]));
            Assert.That(await second, Is.EqualTo(_avatars[ids[0]]));

            await _helix.Received(1).GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task Чужие_имена_и_не_числовые_идентификаторы_не_трогаются()
    {
        Directory.CreateDirectory(_directory);
        var foreign = Path.Combine(_directory, "notes.img");
        var stale = Path.Combine(_directory, "123.img");
        await File.WriteAllTextAsync(foreign, "чужое");
        await File.WriteAllBytesAsync(stale, Png);
        File.SetLastWriteTimeUtc(foreign, _time.UtcNow.UtcDateTime.AddYears(-1));
        File.SetLastWriteTimeUtc(stale, _time.UtcNow.UtcDateTime.AddYears(-1));

        var cache = Create();
        var changed = await cache.ResolveAsync(["../settings", "abc", ""]);
        cache.Invalidate("..\\accounts");
        var cached = cache.GetCachedPaths(["123"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.Empty);
            Assert.That(cached, Is.Empty);
            Assert.That(_batches, Is.Empty);
            Assert.That(File.Exists(foreign), Is.True, "уборка не трогает файл с чужим именем");
            Assert.That(File.Exists(stale), Is.False, "своя давняя картинка без записи убирается по возрасту");
        }
    }

    private static HttpResponseMessage Image(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        return new(HttpStatusCode.OK) { Content = content };
    }

    private string[] Known(int count)
    {
        var ids = Enumerable.Range(1000, count).Select(index => index.ToString()).ToArray();

        foreach (var id in ids)
        {
            _avatars[id] = $"https://static-cdn.jtvnw.net/jtv_user_pictures/{id}-profile_image-300x300.png";
        }

        return ids;
    }

    private UserAvatarCache Create(
        bool cacheOnly = false,
        int maxImageBytes = 1024 * 1024,
        long? maxTotalBytes = null,
        int? maxEntries = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TwitchEndpoints.AvatarClient).Returns(_ => new HttpClient(_cdn, disposeHandler: false));

        var options = new UserAvatarCacheOptions
        {
            Directory = _directory,
            CacheOnly = cacheOnly,
            MaxImageBytes = maxImageBytes,
            MaxTotalBytes = maxTotalBytes ?? UserAvatarCacheOptions.DefaultMaxTotalBytes,
            MaxEntries = maxEntries ?? UserAvatarCacheOptions.DefaultMaxEntries,
        };

        return new(_helix, factory, _time, options, _logger);
    }
}
