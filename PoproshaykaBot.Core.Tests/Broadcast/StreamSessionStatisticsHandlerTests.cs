using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public sealed class StreamSessionStatisticsHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _captured = [];
        _messenger = Substitute.For<IChatMessenger>();
        _streamStatus = Substitute.For<IStreamStatus>();

        _settings = new()
        {
            Twitch =
            {
                Channel = Channel,
                AutoBroadcast =
                {
                    StreamStatusNotificationsEnabled = true,
                    StreamEndStatsMessage = "Итог: {duration} {messages} {chatters}",
                },
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);
        _settingsManager.Current.Returns(_settings);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _eventBus.Subscribe<StreamSessionCompleted>(@event => _captured.Add(@event.Session));

        _timeProvider = new() { UtcNow = StreamStart };

        _tempDirectory = Path.Combine(Path.GetTempPath(), "PoproshaykaBot.SessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new(NullLogger<ActiveStreamSessionStore>.Instance, Path.Combine(_tempDirectory, "active_session.json"));

        _handler = new(_messenger,
            _streamStatus,
            _settingsManager,
            _timeProvider,
            _eventBus,
            _store,
            NullLogger<StreamSessionStatisticsHandler>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _handler.DisposeAsync();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private const string Channel = "test-channel";
    private static readonly DateTime StreamStartUtc = new(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset StreamStart = new(StreamStartUtc);

    private IChatMessenger _messenger = null!;
    private IStreamStatus _streamStatus = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private InMemoryEventBus _eventBus = null!;
    private TestTimeProvider _timeProvider = null!;
    private ActiveStreamSessionStore _store = null!;
    private StreamSessionStatisticsHandler _handler = null!;
    private List<StreamSessionRecord> _captured = null!;
    private string _tempDirectory = null!;

    private static StreamWentOnline Online()
    {
        return new(Channel, new()
        {
            StartedAt = StreamStartUtc,
            ViewerCount = 10,
            Title = "Заголовок",
            GameName = "Игра",
        });
    }

    private static StreamWentOnline OnlineCatchUp()
    {
        return new(Channel, new()
        {
            StartedAt = StreamStartUtc,
            ViewerCount = 10,
            Title = "Заголовок",
            GameName = "Игра",
        }, true);
    }

    private static ChatMessageReceived Chat(string userId, string displayName, bool isBot = false)
    {
        return new(Channel, Guid.NewGuid().ToString("N"), userId, displayName.ToLowerInvariant(), displayName,
            "сообщение", UserStatus.None, false, new(), isBot);
    }

    private void SeedDraft(
        DateTimeOffset startedAt,
        DateTimeOffset updatedAt,
        long messageCount,
        params (string UserId, string DisplayName, long Count)[] chatters)
    {
        _store.Save(new()
        {
            Channel = Channel,
            StartedAt = startedAt,
            UpdatedAt = updatedAt,
            MessageCount = messageCount,
            PeakViewers = 50,
            ViewerSamplesSum = 300,
            ViewerSamplesCount = 10,
            Title = "Черновой заголовок",
            Game = "Черновая игра",
            Chatters = chatters
                .Select(chatter => new ActiveStreamSessionChatter
                {
                    UserId = chatter.UserId,
                    DisplayName = chatter.DisplayName,
                    MessageCount = chatter.Count,
                })
                .ToList(),
        });
    }

    [Test]
    public async Task StreamWentOffline_AfterSession_PublishesCompletedWithAggregateAndChatters()
    {
        await _handler.HandleAsync(Online(), CancellationToken.None);

        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u2", "Bob"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u3", "Carol"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u3", "Carol"), CancellationToken.None);

        _timeProvider.UtcNow = StreamStart.AddHours(2);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));

        var record = _captured[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Channel, Is.EqualTo(Channel));
            Assert.That(record.MessageCount, Is.EqualTo(6));
            Assert.That(record.ChatterCount, Is.EqualTo(3));
            Assert.That(record.PeakViewers, Is.EqualTo(10));
            Assert.That(record.AverageViewers, Is.EqualTo(10));
            Assert.That(record.Title, Is.EqualTo("Заголовок"));
            Assert.That(record.Game, Is.EqualTo("Игра"));
            Assert.That(record.Duration, Is.EqualTo(TimeSpan.FromHours(2)));
            Assert.That(record.Chatters.Select(c => c.DisplayName), Is.EqualTo(new[] { "Alice", "Carol", "Bob" }));
            Assert.That(record.Chatters.Select(c => c.MessageCount), Is.EqualTo(new long[] { 3, 2, 1 }));
            _messenger.Received(1).Send(Arg.Any<string>());
        }
    }

    [Test]
    public async Task StreamWentOffline_AfterSession_DeletesDraftFile()
    {
        await _handler.HandleAsync(Online(), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        _timeProvider.UtcNow = StreamStart.AddHours(1);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_store.Load(), Is.Null);
    }

    [Test]
    public async Task StreamWentOffline_WithIsCatchUpTrue_AndActiveMemorySession_DoesNotPublishOrSend()
    {
        await _handler.HandleAsync(Online(), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);

        await _handler.HandleAsync(new StreamWentOffline(Channel, true), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Is.Empty);
            _messenger.DidNotReceive().Send(Arg.Any<string>());
        }
    }

    [Test]
    public async Task StreamWentOffline_NotificationsDisabled_StillPublishesButDoesNotSend()
    {
        _settings.Twitch.AutoBroadcast.StreamStatusNotificationsEnabled = false;

        await _handler.HandleAsync(Online(), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        _timeProvider.UtcNow = StreamStart.AddMinutes(30);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Has.Count.EqualTo(1));
            Assert.That(_captured[0].MessageCount, Is.EqualTo(1));
            _messenger.DidNotReceive().Send(Arg.Any<string>());
        }
    }

    [Test]
    public async Task StreamWentOffline_WithoutActiveSession_DoesNotPublish()
    {
        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Is.Empty);
    }

    [Test]
    public async Task ChatMessageReceived_FromBot_DoesNotCountTowardSession()
    {
        await _handler.HandleAsync(Online(), CancellationToken.None);

        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("bot", "MyBot", true), CancellationToken.None);
        await _handler.HandleAsync(Chat("bot", "MyBot", true), CancellationToken.None);

        _timeProvider.UtcNow = StreamStart.AddHours(1);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured[0].MessageCount, Is.EqualTo(1));
            Assert.That(_captured[0].ChatterCount, Is.EqualTo(1));
            Assert.That(_captured[0].Chatters.Select(c => c.DisplayName), Is.EqualTo(new[] { "Alice" }));
        }
    }

    [Test]
    public async Task DisposeAsync_WithActiveSession_PersistsDraftForResume()
    {
        await _handler.HandleAsync(Online(), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u2", "Bob"), CancellationToken.None);

        await _handler.DisposeAsync();

        var draft = _store.Load();

        Assert.That(draft, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft!.Channel, Is.EqualTo(Channel));
            Assert.That(draft.StartedAt, Is.EqualTo(StreamStart));
            Assert.That(draft.MessageCount, Is.EqualTo(3));
            Assert.That(draft.Chatters, Has.Count.EqualTo(2));
            Assert.That(draft.Chatters.Single(c => string.Equals(c.UserId, "u1", StringComparison.Ordinal)).MessageCount, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task StreamWentOnline_CatchUp_WithMatchingDraft_ResumesAccumulatedCounters()
    {
        SeedDraft(StreamStart, StreamStart.AddHours(1), 100, ("u1", "Alice", 60), ("u2", "Bob", 40));

        await _handler.HandleAsync(OnlineCatchUp(), CancellationToken.None);

        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u3", "Carol"), CancellationToken.None);

        _timeProvider.UtcNow = StreamStart.AddHours(3);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));

        var record = _captured[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.MessageCount, Is.EqualTo(102));
            Assert.That(record.ChatterCount, Is.EqualTo(3));
            Assert.That(record.PeakViewers, Is.EqualTo(50));
            Assert.That(record.AverageViewers, Is.EqualTo(30));
            Assert.That(record.StartedAt, Is.EqualTo(StreamStart));
            Assert.That(record.Duration, Is.EqualTo(TimeSpan.FromHours(3)));
            Assert.That(record.Chatters.Select(c => c.DisplayName), Is.EqualTo(new[] { "Alice", "Bob", "Carol" }));
            Assert.That(record.Chatters.Select(c => c.MessageCount), Is.EqualTo(new long[] { 61, 40, 1 }));
        }
    }

    [Test]
    public async Task StreamWentOnline_CatchUp_WithoutDraft_StartsFreshSession()
    {
        await _handler.HandleAsync(OnlineCatchUp(), CancellationToken.None);

        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);
        await _handler.HandleAsync(Chat("u2", "Bob"), CancellationToken.None);

        _timeProvider.UtcNow = StreamStart.AddHours(1);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(_captured[0].MessageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task StreamWentOnline_CatchUp_WithDifferentStreamDraft_FinalizesOldAndStartsFresh()
    {
        var oldStart = StreamStart.AddDays(-1);
        SeedDraft(oldStart, oldStart.AddHours(2), 77, ("u9", "Veteran", 77));

        await _handler.HandleAsync(OnlineCatchUp(), CancellationToken.None);

        await _handler.HandleAsync(Chat("u1", "Alice"), CancellationToken.None);

        _timeProvider.UtcNow = StreamStart.AddHours(1);

        await _handler.HandleAsync(new StreamWentOffline(Channel), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured[0].MessageCount, Is.EqualTo(77));
            Assert.That(_captured[0].StartedAt, Is.EqualTo(oldStart));
            Assert.That(_captured[0].Duration, Is.EqualTo(TimeSpan.FromHours(2)));
            Assert.That(_captured[1].MessageCount, Is.EqualTo(1));
            Assert.That(_captured[1].StartedAt, Is.EqualTo(StreamStart));
        }
    }

    [Test]
    public async Task StreamWentOffline_CatchUp_WithDraft_FinalizesDraftFromDisk()
    {
        SeedDraft(StreamStart, StreamStart.AddHours(4), 55, ("u1", "Alice", 30), ("u2", "Bob", 25));

        await _handler.HandleAsync(new StreamWentOffline(Channel, true), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));

        var record = _captured[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.MessageCount, Is.EqualTo(55));
            Assert.That(record.ChatterCount, Is.EqualTo(2));
            Assert.That(record.StartedAt, Is.EqualTo(StreamStart));
            Assert.That(record.EndedAt, Is.EqualTo(StreamStart.AddHours(4)));
            Assert.That(record.Duration, Is.EqualTo(TimeSpan.FromHours(4)));
            Assert.That(_store.Load(), Is.Null);
            _messenger.DidNotReceive().Send(Arg.Any<string>());
        }
    }

    [Test]
    public async Task StreamWentOffline_CatchUp_WithoutDraft_DoesNothing()
    {
        await _handler.HandleAsync(new StreamWentOffline(Channel, true), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Is.Empty);
            _messenger.DidNotReceive().Send(Arg.Any<string>());
        }
    }
}
