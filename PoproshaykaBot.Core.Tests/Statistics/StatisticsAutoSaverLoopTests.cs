using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class StatisticsAutoSaverLoopTests
{
    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private string _directory = null!;
    private string _usersFilePath = null!;
    private string _writeBlockerPath = null!;
    private UserStatisticsRepository _userRepository = null!;
    private StatisticsAutoSaver _autoSaver = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-autosave-loop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _usersFilePath = Path.Combine(_directory, "user_statistics_catalog.json");
        _writeBlockerPath = _usersFilePath + ".tmp";

        var graph = new StatisticsTestGraph(_directory, autoSaveInterval: TimeSpan.FromMilliseconds(50));
        _userRepository = graph.UserRepository;
        _autoSaver = graph.AutoSaver;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (Directory.Exists(_writeBlockerPath))
        {
            Directory.Delete(_writeBlockerPath);
        }

        try
        {
            await _autoSaver.DisposeAsync();
        }
        catch (InvalidOperationException)
        {
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Отказ_записи_не_убивает_цикл_и_не_оставляет_обещание_следующего_запуска()
    {
        Directory.CreateDirectory(_writeBlockerPath);

        await _autoSaver.StartAsync(NullProgress, CancellationToken.None);
        _userRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");

        await WaitForAsync(() => _autoSaver.LastError is not null, "цикл не отметил отказ записи в LastError");

        var frozenNextRunAt = _autoSaver.NextRunAt ?? throw new AssertionException("цикл после отказа записи не назначил следующий запуск");

        Directory.Delete(_writeBlockerPath);

        await WaitForAsync(() => _autoSaver.LastError is null && File.Exists(_usersFilePath),
            "цикл не сохранил статистику после отказа – значит, он умер на первом же сбое");

        Assert.Multiple(() =>
        {
            Assert.That(_autoSaver.NextRunAt, Is.Not.Null);
            Assert.That(_autoSaver.NextRunAt, Is.GreaterThan(frozenNextRunAt));
            Assert.That(_autoSaver.LastRunAt, Is.Not.Null);
        });
    }

    private static async Task WaitForAsync(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail(message);
    }
}
