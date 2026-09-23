using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.Core.Tests.Twitch.Chat;

[TestFixture]
public sealed class ChatIngestionServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _eventSubClient = Substitute.For<ITwitchEventSubClient>();

        _helix = Substitute.For<ITwitchHelixClient>();
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("sub-id");

        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(BroadcasterId);

        _botUserIdProvider = Substitute.For<IBotUserIdProvider>();
        _botUserIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(BotId);

        _settings = new()
        {
            Twitch =
            {
                Channel = "test-channel",
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);

        _settingsManager.Current.Returns(_settings);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _timeProvider = new() { UtcNow = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero) };

        _service = new(_eventSubClient,
            _helix,
            _broadcasterIdProvider,
            _botUserIdProvider,
            _settingsManager,
            _eventBus,
            _timeProvider,
            NullLogger<ChatIngestionService>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.StopAsync(NullProgress, CancellationToken.None);
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });
    private const string BroadcasterId = "12345";
    private const string BotId = "67890";

    private ITwitchEventSubClient _eventSubClient = null!;
    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private IBotUserIdProvider _botUserIdProvider = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private InMemoryEventBus _eventBus = null!;
    private TestTimeProvider _timeProvider = null!;
    private ChatIngestionService _service = null!;

    [Test]
    public async Task Приём_чата_сообщает_о_каждом_подключении_и_разрыве_ровно_один_раз()
    {
        var facts = new List<string>();
        _eventBus.Subscribe<ChatIngestionStarted>(@event => facts.Add($"start {@event.At:HH:mm}"));
        _eventBus.Subscribe<ChatIngestionStopped>(@event => facts.Add($"stop {@event.At:HH:mm}"));

        _eventSubClient.SessionId.Returns("session-1");
        await _service.StartAsync(NullProgress, CancellationToken.None);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(10);

        _eventSubClient.OnDisconnected += Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(
            new EventSubDisconnectedArgs("обрыв"),
            CancellationToken.None);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(1);

        _eventSubClient.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-2", null),
            CancellationToken.None);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(20);

        await _service.StopAsync(NullProgress, CancellationToken.None);
        await _service.StopAsync(NullProgress, CancellationToken.None);

        Assert.That(facts, Is.EqualTo(new[] { "start 18:00", "stop 18:10", "start 18:11", "stop 18:31" }));
    }

    [Test]
    public async Task Остановка_удаляет_подписку_последней_сессии_по_id_из_ответа_на_создание()
    {
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("sub-1", "sub-2");

        _eventSubClient.SessionId.Returns("session-1");
        await _service.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnDisconnected += Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(
            new EventSubDisconnectedArgs("обрыв"),
            CancellationToken.None);

        _eventSubClient.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-2", null),
            CancellationToken.None);

        await _service.StopAsync(NullProgress, CancellationToken.None);
        await _service.StopAsync(NullProgress, CancellationToken.None);

        await _helix.Received(1).DeleteEventSubSubscriptionAsync("sub-2", Arg.Any<CancellationToken>());
        await _helix.DidNotReceive().DeleteEventSubSubscriptionAsync("sub-1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Остановка_после_переиспользованной_подписки_ничего_не_удаляет()
    {
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HelixRequestException(HttpMethod.Post,
                "/helix/eventsub/subscriptions",
                HttpStatusCode.Conflict,
                "subscription already exists",
                null));

        _eventSubClient.SessionId.Returns("session-1");
        await _service.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_service.IsJoined, Is.True);

        await _service.StopAsync(NullProgress, CancellationToken.None);

        await _helix.DidNotReceive().DeleteEventSubSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IEnumerable<TestCaseData> DeleteFailures()
    {
        yield return new TestCaseData(new HelixRequestException(HttpMethod.Delete,
            "/helix/eventsub/subscriptions",
            HttpStatusCode.InternalServerError,
            "oops",
            "{\"access_token\":\"секрет\"}")).SetArgDisplayNames("HTTP 500");

        yield return new TestCaseData(new HttpRequestException("сеть недоступна")).SetArgDisplayNames("сеть");
        yield return new TestCaseData(new OperationCanceledException()).SetArgDisplayNames("таймаут остановки");
    }

    [TestCaseSource(nameof(DeleteFailures))]
    public async Task Отказ_удаления_подписки_не_роняет_остановку(Exception failure)
    {
        var stops = 0;
        _eventBus.Subscribe<ChatIngestionStopped>(_ => stops++);

        _helix.DeleteEventSubSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        _eventSubClient.SessionId.Returns("session-1");
        await _service.StartAsync(NullProgress, CancellationToken.None);

        Assert.DoesNotThrowAsync(() => _service.StopAsync(NullProgress, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stops, Is.EqualTo(1));
            Assert.That(_service.IsJoined, Is.False);
        }

        await _helix.Received(1).DeleteEventSubSubscriptionAsync("sub-id", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Подписка_завершившаяся_после_остановки_не_открывает_приём_и_удаляется()
    {
        var starts = 0;
        _eventBus.Subscribe<ChatIngestionStarted>(_ => starts++);

        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _helix.CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        _eventSubClient.SessionId.Returns("session-1");
        var start = _service.StartAsync(NullProgress, CancellationToken.None);

        await _service.StopAsync(NullProgress, CancellationToken.None);

        await _helix.DidNotReceive().DeleteEventSubSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        pending.SetResult("sub-late");
        await start;

        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.Zero);
            Assert.That(_service.IsJoined, Is.False);
        });

        await _helix.Received(1).DeleteEventSubSubscriptionAsync("sub-late", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Отзыв_подписки_на_чат_заканчивает_приём()
    {
        var stops = 0;
        _eventBus.Subscribe<ChatIngestionStopped>(_ => stops++);

        _eventSubClient.SessionId.Returns("session-1");
        await _service.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(
            new EventSubRevocationArgs("sub-other", "stream.online", "authorization_revoked"),
            CancellationToken.None);

        Assert.That(stops, Is.Zero);

        _eventSubClient.OnRevocation += Raise.Event<EventSubAsyncHandler<EventSubRevocationArgs>>(
            new EventSubRevocationArgs("sub-id", "channel.chat.message", "authorization_revoked"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stops, Is.EqualTo(1));
            Assert.That(_service.IsJoined, Is.False);
        });
    }

    [Test]
    public async Task StartAsync_WhenSessionAlreadyActive_CreatesChatSubscriptionImmediately()
    {
        _eventSubClient.SessionId.Returns("existing-session");

        await _service.StartAsync(NullProgress, CancellationToken.None);

        await _helix.Received(1)
            .CreateEventSubSubscriptionAsync("channel.chat.message",
                "1",
                Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                    d["broadcaster_user_id"] == BroadcasterId
                    && d["user_id"] == BotId),
                "existing-session",
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Новая_сессия_гасит_признак_подключения_к_чату_до_попыток_подписки()
    {
        _eventSubClient.SessionId.Returns("session-1");

        await _service.StartAsync(NullProgress, CancellationToken.None);

        Assert.That(_service.IsJoined, Is.True);

        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(string.Empty);

        _eventSubClient.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-2", null),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsJoined, Is.False);
            Assert.That(_service.JoinedAt, Is.Null);
        });
    }

    [Test]
    public async Task StartAsync_WhenNoActiveSession_DoesNotCallHelixUntilWelcome()
    {
        _eventSubClient.SessionId.Returns((string?)null);

        await _service.StartAsync(NullProgress, CancellationToken.None);

        await _helix.DidNotReceive()
            .CreateEventSubSubscriptionAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }
}
