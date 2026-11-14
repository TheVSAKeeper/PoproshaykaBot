using PoproshaykaBot.Core.Platforms;
using System.Text.Json;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class CommandUsageStatisticsTests
{
    private const string UsageFileName = "command-usage.json";

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _time = new()
        {
            UtcNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
        };
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }

    private string _directory = null!;
    private TestTimeProvider _time = null!;

    [Test]
    public async Task Счётчики_команд_переживают_перезапуск()
    {
        var repository = new CommandUsageRepository(_time);
        var saver = CreateSaver(repository);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        repository.Track("ранг", "Алиса");
        repository.Track("ранг", "Борис");

        await saver.StopAsync(new Progress<string>(), CancellationToken.None);

        var restored = new CommandUsageRepository(_time);
        var restoredSaver = CreateSaver(restored);

        await restoredSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            var record = restored.GetSnapshot().Single();

            Assert.Multiple(() =>
            {
                Assert.That(record.Canonical, Is.EqualTo("ранг"));
                Assert.That(record.TotalCount, Is.EqualTo(2));
                Assert.That(record.LastUsedBy, Is.EqualTo("Борис"));
            });
        }
        finally
        {
            await restoredSaver.DisposeAsync();
        }
    }

    [Test]
    public async Task Непрочитанный_файл_команд_не_перезаписывается()
    {
        var path = Path.Combine(_directory, UsageFileName);
        await File.WriteAllTextAsync(path, "{ это не json");

        var repository = new CommandUsageRepository(_time);
        var saver = CreateSaver(repository);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        repository.Track("ранг", "Алиса");

        await saver.SaveNowAsync();
        await saver.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ это не json"),
                "Файл, который не удалось прочитать, не должен быть перезаписан пустым репозиторием");

            Assert.That(Directory.GetFiles(_directory, "*invalid*"), Is.Not.Empty,
                "Рядом с повреждённым файлом должна остаться копия");
        });
    }

    [Test]
    public async Task Старт_стрима_обнуляет_счётчик_за_стрим_и_не_трогает_общий()
    {
        var repository = new CommandUsageRepository(_time);
        var bus = CreateBus(repository, out var handler);

        repository.Track("ранг", "Алиса");

        await bus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, "test-channel", new StreamInfo
        {
            Id = "stream-1",
        }, DateTimeOffset.UtcNow));

        var record = repository.GetSnapshot().Single();

        Assert.Multiple(() =>
        {
            Assert.That(record.TotalCount, Is.EqualTo(1));
            Assert.That(record.StreamCount, Is.Zero);
        });

        handler.Dispose();
    }

    [Test]
    public async Task Повторное_событие_того_же_стрима_счётчик_за_стрим_не_сбрасывает()
    {
        var repository = new CommandUsageRepository(_time);
        var bus = CreateBus(repository, out var handler);

        var broadcastId = Guid.NewGuid();
        var stream = new StreamInfo
        {
            Id = "stream-1",
        };

        await bus.PublishAsync(new BroadcastStarted(broadcastId, StreamingPlatform.Twitch, "test-channel", stream, DateTimeOffset.UtcNow));

        repository.Track("ранг", "Алиса");

        await bus.PublishAsync(new BroadcastStarted(broadcastId, StreamingPlatform.Twitch, "test-channel", stream, DateTimeOffset.UtcNow, true));

        Assert.That(repository.GetSnapshot().Single().StreamCount, Is.EqualTo(1),
            "Догоняющее событие того же стрима не должно терять счёт этого стрима");

        handler.Dispose();
    }

    [Test]
    public async Task Загрузка_после_старта_стрима_не_возвращает_счёт_прошлого_стрима()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, UsageFileName),
            """
            {"streamId":"stream-1","commands":[{"canonical":"ранг","totalCount":7,"streamCount":7}]}
            """);

        var repository = new CommandUsageRepository(_time);
        var bus = CreateBus(repository, out var handler);

        await bus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, "test-channel", new StreamInfo
        {
            Id = "stream-2",
        }, DateTimeOffset.UtcNow));

        var saver = CreateSaver(repository);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        var record = repository.GetSnapshot().Single();

        Assert.Multiple(() =>
        {
            Assert.That(record.TotalCount, Is.EqualTo(7), "Общий счёт обязан прийти с диска.");
            Assert.That(record.StreamCount, Is.Zero,
                "Стрим уже начался до чтения файла – счёт за стрим прошлого стрима возвращать нельзя.");
        });

        await saver.StopAsync(new Progress<string>(), CancellationToken.None);
        handler.Dispose();
    }

    [Test]
    public async Task Пустой_список_команд_в_файле_не_роняет_загрузку()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, UsageFileName), """{"streamId":"stream-1","commands":null}""");

        var repository = new CommandUsageRepository(_time);
        var saver = CreateSaver(repository);

        Assert.DoesNotThrowAsync(async () => await saver.StartAsync(new Progress<string>(), CancellationToken.None),
            "Валидный JSON без списка команд не имеет права срывать подключение бота.");

        Assert.That(repository.GetSnapshot(), Is.Empty);

        await saver.StopAsync(new Progress<string>(), CancellationToken.None);
    }

    [Test]
    public async Task Отказ_записи_пользователей_не_теряет_счётчики_команд()
    {
        var repository = new CommandUsageRepository(_time);
        var graph = new StatisticsTestGraph(_directory, commandUsageRepository: repository);
        var users = graph.UserRepository;
        var saver = graph.AutoSaver;

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);

        users.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "alice");
        repository.Track("ранг", "Алиса");

        Directory.CreateDirectory(Path.Combine(_directory, "users_statistics.json"));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await saver.SaveNowAsync());

        Assert.That(repository.HasChanges, Is.True,
            "Снимок счётчиков снят, но не записан – право на запись обязано вернуться, иначе счётчики теряются молча.");

        Directory.Delete(Path.Combine(_directory, "users_statistics.json"));

        await saver.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_directory, UsageFileName)), Is.True,
            "Следующее сохранение обязано дописать счётчики, которые прошлый отказ оставил в памяти.");
    }

    [TestCase(new[] { "profile" }, 7, false)]
    [TestCase(new[] { "Profile" }, 7, false)]
    [TestCase(new[] { "profile", "пресет" }, 3, true)]
    [TestCase(new[] { "ранг" }, 0, false)]
    public async Task Счётчик_прежнего_имени_переезжает_к_пресету_только_когда_своего_у_него_нет(
        string[] canonicals,
        int presetTotal,
        bool profileKept)
    {
        var path = Path.Combine(_directory, UsageFileName);
        var data = new CommandUsageData
        {
            BroadcastId = "s1",
        };

        data.Commands.AddRange(canonicals.Select(canonical => new CommandUsageRecord
        {
            Canonical = canonical,
            TotalCount = canonical == "пресет" ? 3 : 7,
            StreamCount = 1,
            LastUsedBy = "Алиса",
        }));

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, JsonStoreOptions.Default));

        var repository = new CommandUsageRepository(_time);
        var saver = CreateSaver(repository);

        await saver.StartAsync(new Progress<string>(), CancellationToken.None);
        repository.Track("пресет", "Борис");
        await saver.StopAsync(new Progress<string>(), CancellationToken.None);

        var written = JsonSerializer.Deserialize<CommandUsageData>(await File.ReadAllTextAsync(path), JsonStoreOptions.Default)!;
        var byCanonical = written.Commands.ToDictionary(record => record.Canonical, StringComparer.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(byCanonical["пресет"].TotalCount, Is.EqualTo(presetTotal + 1));
            Assert.That(byCanonical.ContainsKey("profile"), Is.EqualTo(profileKept));
            Assert.That(byCanonical.ContainsKey("мойпрофиль"), Is.False, "Прежний счёт не достаётся команде, забравшей токен");
            Assert.That(byCanonical.ContainsKey("ранг"), Is.EqualTo(canonicals.Contains("ранг")));
        });
    }

    private static InMemoryEventBus CreateBus(CommandUsageRepository repository, out CommandUsageStreamResetHandler handler)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        handler = new(repository, bus, NullLogger<CommandUsageStreamResetHandler>.Instance);
        return bus;
    }

    private StatisticsAutoSaver CreateSaver(CommandUsageRepository repository)
    {
        return new StatisticsTestGraph(_directory, commandUsageRepository: repository).AutoSaver;
    }
}
