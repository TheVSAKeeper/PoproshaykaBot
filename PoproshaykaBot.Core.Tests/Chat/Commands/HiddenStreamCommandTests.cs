using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public sealed class HiddenStreamCommandTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stream-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(_tempDir, "broadcasts.json"),
            Path.Combine(_tempDir, "stream_sessions.json"));

        var visible = Record(StreamingPlatform.Twitch, "Видимый", 10, 5, DateTimeOffset.UtcNow.AddHours(-5));
        var hidden = Record(StreamingPlatform.Twitch, HiddenTitle, HiddenNumber, (int)HiddenNumber, DateTimeOffset.UtcNow.AddHours(-2));

        _store.TryAppend(visible);
        _store.TryAppend(hidden);
        _store.TrySetHidden(hidden.Id, true);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private const string HiddenTitle = "СкрытыйНаплывБотов";
    private const long HiddenNumber = 9999;

    private static readonly TestCaseData[] Commands =
    [
        new TestCaseData(
            (Func<BroadcastHistoryStore, IChatCommand>)(store => new LastStreamCommand(store)),
            "Видимый").SetName("Последний стрим"),
        new TestCaseData(
            (Func<BroadcastHistoryStore, IChatCommand>)(store => new PeakStreamCommand(store)),
            "👥 5").SetName("Рекорд стрима"),
        new TestCaseData(
            (Func<BroadcastHistoryStore, IChatCommand>)(store => new StreamsCountCommand(store, TimeProvider.System)),
            "Всего стримов: 1").SetName("Счётчик стримов"),
        new TestCaseData(
            (Func<BroadcastHistoryStore, IChatCommand>)(store => new TopStreamsCommand(store)),
            "1. Видимый").SetName("Топ стримов"),
        new TestCaseData(
            (Func<BroadcastHistoryStore, IChatCommand>)(store => new WasThereCommand(store)),
            "на 1 стримах из 1").SetName("Я там был"),
    ];

    private BroadcastHistoryStore _store = null!;
    private string _tempDir = null!;

    private static BroadcastRecord Record(
        StreamingPlatform platform,
        string title,
        long messages,
        int peakViewers,
        DateTimeOffset startedAt,
        string userId = "u1")
    {
        var endedAt = startedAt.AddHours(1);

        return new BroadcastRecord
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            PeakViewers = peakViewers,
            AverageViewers = 1,
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = platform,
                    Channel = "chan",
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                    Title = title,
                    Game = "Игра",
                    MessageCount = messages,
                    ChatterCount = 1,
                    PeakViewers = peakViewers,
                    AverageViewers = 1,
                    Chatters =
                    [
                        new() { UserId = userId, DisplayName = "Alice", MessageCount = messages },
                    ],
                },
            ],
        };
    }

    [TestCaseSource(nameof(Commands))]
    public async Task Скрытый_стрим_не_попадает_в_ответ(Func<BroadcastHistoryStore, IChatCommand> factory, string expected)
    {
        var command = factory(_store);

        var response = await command.ExecuteAsync(new()
        {
            Platform = StreamingPlatform.Twitch,
            MessageId = "m1",
            UserId = "u1",
            Username = "alice",
            DisplayName = "Alice",
            Arguments = [],
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Text, Does.Not.Contain(HiddenTitle));
            Assert.That(response.Text, Does.Not.Contain(HiddenNumber.ToString()));
            Assert.That(response.Text, Does.Contain(expected));
        }
    }

    [Test]
    public void LoadVisible_Отдаёт_только_видимые_а_Load_всю_историю()
    {
        var visible = _store.LoadVisible().Broadcasts;
        var all = _store.Load().Broadcasts;

        visible.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all.Exists(b => b.IsHidden), Is.True);
            Assert.That(_store.LoadVisible().Broadcasts.Select(b => b.Title), Is.EquivalentTo(new[] { "Видимый" }));
        }
    }

    [Test]
    public async Task YouTubeOnly_Broadcast_IsVisibleToAllFiveCommands_And_WasThereDistinguishesPlatform()
    {
        var dir = Path.Combine(_tempDir, "yt-only");
        Directory.CreateDirectory(dir);
        var store = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(dir, "broadcasts.json"),
            Path.Combine(dir, "stream_sessions.json"));

        var ytBroadcast = Record(StreamingPlatform.YouTube, "YouTubeOnly", 42, 77, DateTimeOffset.UtcNow.AddHours(-3), userId: "same-id");
        store.TryAppend(ytBroadcast);

        var last = await new LastStreamCommand(store).ExecuteAsync(CreateContext(StreamingPlatform.YouTube, "same-id"), CancellationToken.None);
        var peak = await new PeakStreamCommand(store).ExecuteAsync(CreateContext(StreamingPlatform.Twitch, "same-id"), CancellationToken.None);
        var count = await new StreamsCountCommand(store, TimeProvider.System).ExecuteAsync(CreateContext(StreamingPlatform.Twitch, "same-id"), CancellationToken.None);
        var top = await new TopStreamsCommand(store).ExecuteAsync(CreateContext(StreamingPlatform.YouTube, "same-id"), CancellationToken.None);
        var wasThereYt = await new WasThereCommand(store).ExecuteAsync(CreateContext(StreamingPlatform.YouTube, "same-id"), CancellationToken.None);
        var wasThereTwitch = await new WasThereCommand(store).ExecuteAsync(CreateContext(StreamingPlatform.Twitch, "same-id"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(last!.Text, Does.Contain("YouTubeOnly"));
            Assert.That(peak!.Text, Does.Contain("77"));
            Assert.That(count!.Text, Does.Contain("Всего стримов: 1"));
            Assert.That(top!.Text, Does.Contain("YouTubeOnly"));
            Assert.That(wasThereYt!.Text, Does.Contain("на 1 стримах из 1"));
            Assert.That(wasThereTwitch!.Text, Does.Contain("ни на одном"));
            Assert.That(store.LoadSessionsProjection().Sessions, Is.Empty);
        });
    }

    private static CommandContext CreateContext(StreamingPlatform platform, string userId)
    {
        return new()
        {
            Platform = platform,
            MessageId = "m1",
            UserId = userId,
            Username = "alice",
            DisplayName = "Alice",
            Arguments = [],
        };
    }
}
