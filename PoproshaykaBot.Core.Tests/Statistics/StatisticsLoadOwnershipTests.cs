using PoproshaykaBot.Core.Platforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class StatisticsLoadOwnershipTests
{
    private const string UsersFileName = "users_statistics.json";
    private const string BotFileName = "bot_statistics.json";
    private const string HistoryFileName = "broadcasts.json";
    private const string SessionsProjectionFileName = "stream_sessions.json";
    private const string CommandUsageFileName = "command-usage.json";

    private const string UsersContent = """
                                        [
                                          {
                                            "userId": "u1",
                                            "name": "Alice",
                                            "messageCount": 100,
                                            "bonusPoints": 5,
                                            "penaltyPoints": 2,
                                            "firstSeen": "2024-01-01T00:00:00Z",
                                            "lastSeen": "2024-01-02T00:00:00Z"
                                          }
                                        ]
                                        """;

    private const string BotContent = """{"totalMessages":7,"startTime":"2024-01-01T00:00:00Z"}""";

    private const string BrokenContent = "{ это не json";

    private string _directory = string.Empty;
    private string _source = string.Empty;

    [SetUp]
    public void SetUp()
    {
        var root = Path.Combine(Path.GetTempPath(), "PoproshaykaBot-StatisticsLoad-" + Guid.NewGuid().ToString("N"));

        _directory = Path.Combine(root, "data");
        _source = Path.Combine(root, "source");

        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_source);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_directory)!, true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public async Task Подключение_бота_не_перечитывает_уже_загруженную_статистику_пользователей()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, UsersFileName), UsersContent);

        var (users, _, _, loader, saver) = CreateGraph();

        await loader.EnsureLoadedAsync();
        users.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            Assert.That(users.ForPlatform(StreamingPlatform.Twitch).GetById("u1")!.MessageCount, Is.EqualTo(101ul),
                "Подключение бота не должно ни обнулять счётчики до файла, ни складывать их с файлом");
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Сохранение_до_подключения_бота_не_затирает_статистику_бота()
    {
        var usersPath = Path.Combine(_directory, UsersFileName);
        var botPath = Path.Combine(_directory, BotFileName);

        await File.WriteAllTextAsync(usersPath, UsersContent);
        await File.WriteAllTextAsync(botPath, BotContent);

        var (users, _, fileStore, loader, saver) = CreateGraph();

        await loader.EnsureLoadedAsync();
        users.IncrementBonusPoints(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), 10);

        await saver.SaveNowAsync();

        var savedCatalog = await fileStore.LoadUserStatisticsCatalogAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(botPath), Is.EqualTo(BotContent),
                "Статистика бота не загружалась – её файл нельзя переписывать пустым репозиторием");
            Assert.That(savedCatalog.Value!.UnattributedCounters.Single().BonusPoints, Is.EqualTo(15ul),
                "Загруженная статистика пользователей обязана сохраняться и до подключения бота");
        }
    }

    [TestCase(UsersFileName)]
    [TestCase(BotFileName)]
    public async Task Непрочитанный_файл_статистики_не_переписывается_пустым_репозиторием(string brokenFileName)
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, UsersFileName),
            string.Equals(brokenFileName, UsersFileName, StringComparison.Ordinal) ? BrokenContent : UsersContent);

        await File.WriteAllTextAsync(Path.Combine(_directory, BotFileName),
            string.Equals(brokenFileName, BotFileName, StringComparison.Ordinal) ? BrokenContent : BotContent);

        var (users, bot, _, loader, saver) = CreateGraph();

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            users.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u2"), "Bob");
            bot.IncrementMessagesProcessed();

            await saver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await File.ReadAllTextAsync(Path.Combine(_directory, brokenFileName)), Is.EqualTo(BrokenContent),
                    "Сорвавшееся чтение – не пустота: файл обязан остаться тем, чем был");
                Assert.That(BackupsOf(brokenFileName), Is.Not.Empty,
                    "О повреждённом файле пользователь узнаёт по копии с суффиксом invalid рядом с ним");
                Assert.That(loader.IsLoadedFor(StreamingPlatform.Twitch), Is.EqualTo(!string.Equals(brokenFileName, UsersFileName, StringComparison.Ordinal)),
                    "Флаг «загружено» поднимает только удачное чтение");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Отсутствующий_файл_статистики_это_законная_пустота()
    {
        var (users, _, fileStore, loader, saver) = CreateGraph();

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            users.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u3"), "Carol");

            await saver.SaveNowAsync();

            var savedCatalog = await fileStore.LoadUserStatisticsCatalogAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(loader.IsLoadedFor(StreamingPlatform.Twitch), Is.True,
                    "Свежий профиль без файла – это ноль записей, а не отказ чтения");
                Assert.That(savedCatalog.Value!.Accounts.Single().Name, Is.EqualTo("Carol"),
                    "Накопленное в первом сеансе обязано дойти до диска");
                Assert.That(File.Exists(Path.Combine(_directory, BotFileName)), Is.True,
                    "Статистика бота тоже заводится с нуля, а не ждёт файла");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Сорвавшееся_повторное_чтение_статистики_бота_гасит_флаг_прошлой_загрузки()
    {
        var botPath = Path.Combine(_directory, BotFileName);

        await File.WriteAllTextAsync(Path.Combine(_directory, UsersFileName), UsersContent);
        await File.WriteAllTextAsync(botPath, BotContent);

        var (_, bot, _, _, saver) = CreateGraph();

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);
        await saver.StopAsync(new Progress<string>(), CancellationToken.None);

        await File.WriteAllTextAsync(botPath, BrokenContent);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            bot.IncrementMessagesProcessed();

            await saver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await File.ReadAllTextAsync(botPath), Is.EqualTo(BrokenContent),
                    "Флаг удачной загрузки прошлого подключения не даёт права переписать непрочитанный файл");
                Assert.That(BackupsOf(BotFileName), Is.Not.Empty,
                    "О повреждённом файле пользователь узнаёт по копии с суффиксом invalid рядом с ним");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Сорвавшееся_чтение_статистики_пользователей_не_повторяется_на_следующем_подключении()
    {
        var usersPath = Path.Combine(_directory, UsersFileName);

        await File.WriteAllTextAsync(usersPath, BrokenContent);
        await File.WriteAllTextAsync(Path.Combine(_directory, BotFileName), BotContent);

        var (users, _, _, loader, saver) = CreateGraph();

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);
        await saver.StopAsync(new Progress<string>(), CancellationToken.None);

        await File.WriteAllTextAsync(usersPath, UsersContent);

        users.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u9"), "Zed");

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            await saver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(users.ForPlatform(StreamingPlatform.Twitch).GetById("u9")!.MessageCount, Is.EqualTo(1ul),
                    "Повторная попытка чтения выбросила бы всё, что накопилось после отказа");
                Assert.That(users.ForPlatform(StreamingPlatform.Twitch).GetById("u1"), Is.Null,
                    "Отказ чтения не ретраится: файл в этом сеансе больше не читают, даже если он стал читаемым");
                Assert.That(loader.IsLoadedFor(StreamingPlatform.Twitch), Is.False,
                    "Флаг остаётся опущенным – сохранять поверх файла, который не удалось прочитать, нельзя");
                Assert.That(await File.ReadAllTextAsync(usersPath), Is.EqualTo(UsersContent),
                    "Накопленное в сеансе не ложится поверх файла, содержимое которого приложению неизвестно");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Сорвавшаяся_запись_истории_дотаскивается_ближайшим_тиком_автосохранения()
    {
        var historyPath = Path.Combine(_directory, HistoryFileName);
        var sessionsPath = Path.Combine(_directory, SessionsProjectionFileName);
        var blockedTemporaryPath = historyPath + ".tmp";

        Directory.CreateDirectory(blockedTemporaryPath);

        var history = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, historyPath, sessionsPath);
        var (_, _, _, _, saver) = CreateGraph(history);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            Assert.That(history.TryAppend(Broadcast("конец-стрима")), Is.False,
                "Отказ записи истории сообщает вызывающему через false, оставляя данные в памяти");

            Assert.That(File.Exists(historyPath), Is.False, "Записывать было некуда: файла истории на диске нет");
            Assert.That(history.HasPendingFlush, Is.True);

            Directory.Delete(blockedTemporaryPath);

            await saver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ChannelsOnDisk(historyPath), Is.EqualTo(new[] { "конец-стрима" }),
                    "Признак «файл отстаёт» переживает любой отказ записи, и ближайший тик автосохранения дотаскивает сессию");
                Assert.That(history.TryFlush(), Is.True, "После удачного сброса дописывать больше нечего");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Остановка_без_единой_прочитанной_статистики_дотаскивает_историю()
    {
        var historyPath = Path.Combine(_directory, HistoryFileName);
        var sessionsPath = Path.Combine(_directory, SessionsProjectionFileName);
        var usersPath = Path.Combine(_directory, UsersFileName);
        var blockedTemporaryPath = historyPath + ".tmp";

        await File.WriteAllTextAsync(usersPath, BrokenContent);
        await File.WriteAllTextAsync(Path.Combine(_directory, BotFileName), BrokenContent);
        await File.WriteAllTextAsync(Path.Combine(_directory, CommandUsageFileName), BrokenContent);

        Directory.CreateDirectory(blockedTemporaryPath);

        var history = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, historyPath, sessionsPath);
        var (_, _, _, loader, saver) = CreateGraph(history);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            Assert.That(loader.IsLoadedFor(StreamingPlatform.Twitch), Is.False, "Повреждённый файл пользователей права сохранять не даёт");

            Assert.That(history.TryAppend(Broadcast("конец-стрима")), Is.False,
                "Записывать было некуда, сессия осталась несохранённой в памяти");

            Directory.Delete(blockedTemporaryPath);

            await saver.StopAsync(new Progress<string>(), CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ChannelsOnDisk(historyPath), Is.EqualTo(new[] { "конец-стрима" }),
                    "Остановка бота – последний шанс записать историю, и непрочитанная статистика его не отменяет");
                Assert.That(await File.ReadAllTextAsync(usersPath), Is.EqualTo(BrokenContent),
                    "Сохранять при этом по-прежнему нечего: повреждённый файл статистики остаётся нетронутым");
            }
        }
        finally
        {
            await saver.DisposeAsync();
        }
    }

    [Test]
    public async Task Компонент_жизненного_цикла_истории_эфиров_дотаскивает_отложенную_запись()
    {
        var historyPath = Path.Combine(_directory, HistoryFileName);
        var sessionsPath = Path.Combine(_directory, SessionsProjectionFileName);
        var blockedTemporaryPath = historyPath + ".tmp";

        Directory.CreateDirectory(blockedTemporaryPath);

        var history = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, historyPath, sessionsPath);
        Assert.That(history.TryAppend(Broadcast("отложенный-канал")), Is.False);
        Assert.That(history.HasPendingFlush, Is.True);

        Directory.Delete(blockedTemporaryPath);

        using var lifetime = new BroadcastHistoryLifetimeComponent(
            history,
            TimeProvider.System,
            NullLogger<BroadcastHistoryLifetimeComponent>.Instance);

        await lifetime.StartAsync(CancellationToken.None);
        await lifetime.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(history.HasPendingFlush, Is.False);
            Assert.That(ChannelsOnDisk(historyPath), Is.EqualTo(new[] { "отложенный-канал" }));
        }
    }

    [Test]
    public async Task LoadUserStatisticsCatalog_UnsupportedSchemaVersion_CreatesInvalidBackupAndReturnsFailure()
    {
        var catalogPath = Path.Combine(_directory, SettingsFileNames.UserStatisticsCatalogFileName);
        const string futureCatalog = """{"schemaVersion":99,"accounts":[]}""";
        await File.WriteAllTextAsync(catalogPath, futureCatalog);

        var fileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _directory);
        var result = await fileStore.LoadUserStatisticsCatalogAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(await File.ReadAllTextAsync(catalogPath), Is.EqualTo(futureCatalog));
            Assert.That(BackupsOf(SettingsFileNames.UserStatisticsCatalogFileName), Has.Length.EqualTo(1));
        }
    }

    private static string[] ChannelsOnDisk(string path)
    {
        var history = JsonSerializer.Deserialize<BroadcastHistoryDocument>(File.ReadAllText(path), JsonStoreOptions.Default)!;

        return [.. history.Broadcasts.SelectMany(record => record.Platforms.Select(platform => platform.Channel))];
    }

    private static BroadcastRecord Broadcast(string channel)
    {
        var startedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero).AddDays(channel.Length);

        return BroadcastRecord.FromTwitchSession(new()
        {
            Channel = channel,
            StartedAt = startedAt,
            EndedAt = startedAt.AddHours(2),
            Title = "Эфир",
            MessageCount = 10,
        });
    }

    private string[] BackupsOf(string fileName)
    {
        var pattern = Path.GetFileNameWithoutExtension(fileName) + ".invalid-*" + Path.GetExtension(fileName);

        return Directory.GetFiles(_directory, pattern);
    }

    private (UserStatisticsRepository Users, IBotStatisticsRepository Bot, StatisticsFileStore FileStore, UserStatisticsLoader Loader, StatisticsAutoSaver Saver) CreateGraph(
        params IFlushableHistoryStore[] extraFlushableStores)
    {
        return CreateGraph(saverLogger: null, extraFlushableStores);
    }

    private (UserStatisticsRepository Users, IBotStatisticsRepository Bot, StatisticsFileStore FileStore, UserStatisticsLoader Loader, StatisticsAutoSaver Saver) CreateGraph(
        ILogger<StatisticsAutoSaver>? saverLogger,
        params IFlushableHistoryStore[] extraFlushableStores)
    {
        var graph = new StatisticsTestGraph(_directory, saverLogger: saverLogger, extraFlushableStores: extraFlushableStores);
        return (graph.UserRepository, graph.BotRepository, graph.FileStore, graph.UserLoader, graph.AutoSaver);
    }
}
