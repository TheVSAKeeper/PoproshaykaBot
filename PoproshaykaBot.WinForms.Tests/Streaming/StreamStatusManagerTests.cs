using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Tests.Polls;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Tests.Streaming;

[TestFixture]
public sealed class StreamStatusManagerTests
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

        _settings = new()
        {
            Twitch =
            {
                Channel = "test-channel",
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        _settingsManager.Current.Returns(_settings);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);

        _clock = new() { UtcNow = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero) };

        _manager = new(_eventSubClient,
            _helix,
            _broadcasterIdProvider,
            _settingsManager,
            _eventBus,
            _clock,
            NullLogger<StreamStatusManager>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _manager.DisposeAsync();
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });
    private const string BroadcasterId = "12345";

    private ITwitchEventSubClient _eventSubClient = null!;
    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private InMemoryEventBus _eventBus = null!;
    private TestTimeProvider _clock = null!;
    private StreamStatusManager _manager = null!;

    private static EventSubNotificationArgs StreamOnlineNotification()
    {
        return new("stream.online",
            "1",
            "msg-1",
            DateTime.UtcNow,
            JsonSerializer.SerializeToElement(new { }));
    }

    private static HelixStreamInfo SampleStream(string id = "stream-1")
    {
        return new(id,
            BroadcasterId,
            "bobito217",
            "Bobito217",
            "509658",
            "Just Chatting",
            "live",
            "Тест",
            42,
            DateTime.UtcNow,
            "ru",
            "https://example.com/{width}x{height}.jpg",
            ["Russian"],
            false);
    }

    [Test]
    public async Task StopAsync_DrainsPendingMetadataRetryLoop()
    {
        var firstCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.GetStreamAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                firstCallStarted.TrySetResult();
                return Task.FromResult<HelixStreamInfo?>(null);
            });

        await _manager.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnNotification +=
            Raise.Event<EventSubAsyncHandler<EventSubNotificationArgs>>(StreamOnlineNotification(), CancellationToken.None);

        await firstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(_manager.MetadataRetryTask, Is.Not.Null,
            "после stream.online retry-цикл должен быть зарегистрирован");

        var retryTask = _manager.MetadataRetryTask!;

        await _manager.StopAsync(NullProgress, CancellationToken.None);

        Assert.That(retryTask.IsCompleted, Is.True,
            "StopAsync должен дождаться завершения фонового retry-цикла");
    }

    [Test]
    public async Task OnDisconnected_ClearsCurrentStream()
    {
        _helix.GetStreamAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(SampleStream());

        await _manager.StartAsync(NullProgress, CancellationToken.None);

        var welcome = new EventSubSessionWelcomeArgs("session-1", 60);
        _eventSubClient.OnSessionWelcome +=
            Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(welcome, CancellationToken.None);

        await Task.Delay(50);

        Assert.That(_manager.CurrentStream, Is.Not.Null);

        _eventSubClient.OnDisconnected +=
            Raise.Event<EventSubAsyncHandler<EventSubDisconnectedArgs>>(new EventSubDisconnectedArgs("test"), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(_manager.CurrentStream, Is.Null,
                "OnDisconnected должен обнулить CurrentStream, иначе StreamInfoCommand отдаст устаревшие данные");
        }
    }

    [Test]
    public async Task RefreshLiveSnapshot_DoesNotImmediatelyTransitionOnlineToOffline()
    {
        var stream = SampleStream();
        var streamSequence = new Queue<HelixStreamInfo?>([stream, null]);

        _helix.GetStreamAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(_ => streamSequence.Dequeue());

        var offlineEvents = 0;
        _eventBus.Subscribe<StreamWentOffline>(_ => Interlocked.Increment(ref offlineEvents));

        await _manager.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnSessionWelcome +=
            Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(new EventSubSessionWelcomeArgs("session-1", 60), CancellationToken.None);

        await Task.Delay(50);
        Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Online));

        await _manager.RefreshLiveSnapshotAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Online),
                "первый Offline-ответ от API не должен мгновенно сбрасывать локальный Online");

            Assert.That(offlineEvents, Is.Zero);
        }
    }

    [Test]
    public async Task RefreshLiveSnapshot_ForcesOfflineAfterStuckThreshold()
    {
        var stream = SampleStream();
        _helix.GetStreamAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(stream, null, null);

        var offlineReceived = new TaskCompletionSource<StreamWentOffline>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventBus.Subscribe<StreamWentOffline>(@event => offlineReceived.TrySetResult(@event));

        await _manager.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnSessionWelcome +=
            Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(new EventSubSessionWelcomeArgs("session-1", 60), CancellationToken.None);

        await Task.Delay(50);
        Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Online));

        await _manager.RefreshLiveSnapshotAsync();
        Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Online));

        _clock.UtcNow = _clock.UtcNow.Add(StreamStatusManager.StuckOnlineThreshold + TimeSpan.FromSeconds(1));

        await _manager.RefreshLiveSnapshotAsync();

        Assert.That(_manager.CurrentStatus, Is.EqualTo(StreamStatus.Offline),
            "после превышения StuckOnlineThreshold второй Offline-ответ должен принудительно перевести в Offline");

        var offline = await offlineReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(offline.Channel, Is.EqualTo(_settings.Twitch.Channel));
    }

    [Test]
    public async Task HandleStreamOnline_PublishesStreamMetadataResolved_AfterMetadataFetch()
    {
        var stream = SampleStream("stream-online-1");
        _helix.GetStreamAsync(BroadcasterId, Arg.Any<CancellationToken>()).Returns(stream);

        StreamMetadataResolved? received = null;
        var receivedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventBus.Subscribe<StreamMetadataResolved>(@event =>
        {
            received = @event;
            receivedSignal.TrySetResult();
        });

        await _manager.StartAsync(NullProgress, CancellationToken.None);

        _eventSubClient.OnNotification +=
            Raise.Event<EventSubAsyncHandler<EventSubNotificationArgs>>(StreamOnlineNotification(), CancellationToken.None);

        await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(received, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Stream.Id, Is.EqualTo("stream-online-1"));
            Assert.That(received.Channel, Is.EqualTo(_settings.Twitch.Channel));
        }
    }
}
