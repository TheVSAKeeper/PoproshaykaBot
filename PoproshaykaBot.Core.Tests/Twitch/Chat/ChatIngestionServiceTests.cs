using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

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

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);

        _settingsManager.Current.Returns(_settings);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);

        _service = new(_eventSubClient,
            _helix,
            _broadcasterIdProvider,
            _botUserIdProvider,
            new(),
            _settingsManager,
            _eventBus,
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
    private ChatIngestionService _service = null!;

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
